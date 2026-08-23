using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record ScheduleDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ScheduleStatus Status);

public record ShiftAssignmentDto(
    Guid Id, Guid ScheduleId, Guid EmployeeId, Guid ShiftTypeId,
    DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes,
    decimal NetHours, decimal? LaborCost);

public record ScheduleDetailDto(
    Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ScheduleStatus Status,
    List<ShiftAssignmentDto> Assignments);

public record CreateScheduleRequest([Required, MaxLength(200)] string Name, DateOnly StartDate, DateOnly EndDate);

public record UpdateScheduleRequest(
    [Required, MaxLength(200)] string Name, DateOnly StartDate, DateOnly EndDate, ScheduleStatus Status);

public record CreateAssignmentRequest(
    Guid EmployeeId, Guid ShiftTypeId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes);

public record UpdateAssignmentRequest(
    Guid EmployeeId, Guid ShiftTypeId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes);

[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class SchedulesController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Schedule, ScheduleDto> ToDto =
        s => new ScheduleDto(s.Id, s.Name, s.StartDate, s.EndDate, s.Status);

    private static ShiftAssignmentDto ToAssignmentDto(ShiftAssignment a, decimal? hourlyRate)
    {
        var netHours = WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes);
        return new(a.Id, a.ScheduleId, a.EmployeeId, a.ShiftTypeId, a.Date, a.StartTime, a.EndTime, a.BreakMinutes,
            netHours, WageCalculator.LaborCost(netHours, hourlyRate));
    }

    // issue #14: the contract valid on the assignment's own date, not the schedule's start —
    // a schedule can span a month, long enough for a mid-month contract/rate change.
    private static decimal? HourlyRateOn(IReadOnlyList<Contract> contracts, Guid employeeId, DateOnly date) =>
        contracts
            .Where(c => c.EmployeeId == employeeId && c.ValidFrom <= date && (c.ValidTo is null || c.ValidTo >= date))
            .MaxBy(c => c.ValidFrom)?.HourlyRate;

    [HttpGet("schedules")]
    public async Task<ActionResult<IEnumerable<ScheduleDto>>> GetAll()
    {
        var schedules = await db.Schedules.OrderByDescending(s => s.StartDate).ToListAsync();
        return Ok(schedules.Select(ToDto));
    }

    [HttpGet("schedules/{id:guid}")]
    public async Task<ActionResult<ScheduleDetailDto>> GetById(Guid id)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        var assignments = await db.ShiftAssignments
            .Where(a => a.ScheduleId == id)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();

        var employeeIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var contracts = await db.Contracts.Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();

        return Ok(new ScheduleDetailDto(schedule.Id, schedule.Name, schedule.StartDate, schedule.EndDate,
            schedule.Status,
            assignments.Select(a => ToAssignmentDto(a, HourlyRateOn(contracts, a.EmployeeId, a.Date))).ToList()));
    }

    [HttpPost("schedules")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<ScheduleDto>> Create(CreateScheduleRequest request)
    {
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = schedule.Id }, ToDto(schedule));
    }

    [HttpPut("schedules/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateScheduleRequest request)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        schedule.Name = request.Name;
        schedule.StartDate = request.StartDate;
        schedule.EndDate = request.EndDate;
        schedule.Status = request.Status;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("schedules/{id:guid}/validate")]
    public async Task<ActionResult<Application.Validation.ValidationResult>> Validate(Guid id)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        var assignments = await db.ShiftAssignments.Where(a => a.ScheduleId == id).ToListAsync();
        var employeeIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.Include(e => e.EligibleShiftTypes)
            .Where(e => employeeIds.Contains(e.Id)).ToListAsync();
        var shiftTypes = await db.ShiftTypes.ToListAsync();
        var contracts = await db.Contracts.Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();

        // issues #8/#9: rest-time and consecutive-day rules need shifts outside this
        // Schedule's own date range too (an adjacent week already planned).
        var historyStart = schedule.StartDate.AddDays(-6);
        var historyEnd = schedule.EndDate.AddDays(6);
        var historyAssignments = await db.ShiftAssignments
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Date >= historyStart && a.Date <= historyEnd)
            .ToListAsync();

        // issue #17: only absences overlapping this Schedule's own span matter here — unlike
        // the rest-time/consecutive-days history window above, absences don't need lookback.
        var absences = await db.Absences
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.From <= schedule.EndDate && a.To >= schedule.StartDate)
            .ToListAsync();

        return Ok(ScheduleValidator.Validate(schedule, assignments, employees, shiftTypes, contracts, historyAssignments, absences));
    }

    [HttpPost("schedules/{id:guid}/assignments")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<ShiftAssignmentDto>> CreateAssignment(Guid id, CreateAssignmentRequest request)
    {
        if (!await db.Schedules.AnyAsync(s => s.Id == id))
            return NotFound();

        if (!await db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
            return BadRequest($"Employee '{request.EmployeeId}' does not exist.");

        if (!await db.ShiftTypes.AnyAsync(s => s.Id == request.ShiftTypeId))
            return BadRequest($"Shift type '{request.ShiftTypeId}' does not exist.");

        if (request.EndTime <= request.StartTime)
            return BadRequest("Cross-midnight shift assignments are not supported (issue #11); EndTime must be after StartTime.");

        var assignment = new ShiftAssignment
        {
            Id = Guid.NewGuid(),
            ScheduleId = id,
            EmployeeId = request.EmployeeId,
            ShiftTypeId = request.ShiftTypeId,
            Date = request.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes
        };
        db.ShiftAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var contract = await db.Contracts
            .Where(c => c.EmployeeId == assignment.EmployeeId && c.ValidFrom <= assignment.Date
                && (c.ValidTo == null || c.ValidTo >= assignment.Date))
            .OrderByDescending(c => c.ValidFrom)
            .FirstOrDefaultAsync();

        return CreatedAtAction(nameof(GetById), new { id }, ToAssignmentDto(assignment, contract?.HourlyRate));
    }

    [HttpPut("assignments/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> UpdateAssignment(Guid id, UpdateAssignmentRequest request)
    {
        var assignment = await db.ShiftAssignments.FindAsync(id);
        if (assignment is null)
            return NotFound();

        if (!await db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
            return BadRequest($"Employee '{request.EmployeeId}' does not exist.");

        if (!await db.ShiftTypes.AnyAsync(s => s.Id == request.ShiftTypeId))
            return BadRequest($"Shift type '{request.ShiftTypeId}' does not exist.");

        if (request.EndTime <= request.StartTime)
            return BadRequest("Cross-midnight shift assignments are not supported (issue #11); EndTime must be after StartTime.");

        assignment.EmployeeId = request.EmployeeId;
        assignment.ShiftTypeId = request.ShiftTypeId;
        assignment.Date = request.Date;
        assignment.StartTime = request.StartTime;
        assignment.EndTime = request.EndTime;
        assignment.BreakMinutes = request.BreakMinutes;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("assignments/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> DeleteAssignment(Guid id)
    {
        var assignment = await db.ShiftAssignments.FindAsync(id);
        if (assignment is null)
            return NotFound();

        db.ShiftAssignments.Remove(assignment);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
