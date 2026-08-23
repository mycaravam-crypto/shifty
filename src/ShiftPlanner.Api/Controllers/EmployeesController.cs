using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record EmployeeDto(Guid Id, string PersonnelNumber, string FirstName, string LastName, string? Email, bool Active, Guid? TeamId);

public record CreateEmployeeRequest(
    [Required, MaxLength(50)] string PersonnelNumber,
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [EmailAddress] string? Email,
    Guid? TeamId);

public record UpdateEmployeeRequest(
    [Required, MaxLength(50)] string PersonnelNumber,
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [EmailAddress] string? Email,
    bool Active,
    Guid? TeamId);

[ApiController]
[Route("api/employees")]
[Authorize(Policy = "ApiRead")]
public class EmployeesController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Employee, EmployeeDto> ToDto =
        e => new EmployeeDto(e.Id, e.PersonnelNumber, e.FirstName, e.LastName, e.Email, e.Active, e.TeamId);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll()
    {
        var employees = await db.Employees
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync();
        return Ok(employees.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> GetById(Guid id)
    {
        var employee = await db.Employees.FindAsync(id);
        return employee is null ? NotFound() : Ok(ToDto(employee));
    }

    // issue #18: cumulative over/under-hours balance carried into `before` (defaults to today)
    // from every fully-elapsed Schedule — see HoursBalanceCalculator.
    [HttpGet("{id:guid}/hours-balance")]
    public async Task<ActionResult<decimal>> HoursBalance(Guid id, DateOnly? before)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id))
            return NotFound();

        var cutoff = before ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var schedules = await db.Schedules.Where(s => s.EndDate < cutoff).ToListAsync();
        var scheduleIds = schedules.Select(s => s.Id).ToList();
        var assignments = await db.ShiftAssignments
            .Where(a => a.EmployeeId == id && scheduleIds.Contains(a.ScheduleId)).ToListAsync();
        var contracts = await db.Contracts.Where(c => c.EmployeeId == id).ToListAsync();
        var absences = await db.Absences.Where(a => a.EmployeeId == id).ToListAsync();

        return Ok(HoursBalanceCalculator.CumulativeBalance(id, cutoff, schedules, assignments, contracts, absences));
    }

    [HttpPost]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<EmployeeDto>> Create(CreateEmployeeRequest request)
    {
        if (request.TeamId is { } teamId && !await db.Teams.AnyAsync(t => t.Id == teamId))
            return BadRequest($"Team '{teamId}' does not exist.");

        if (await db.Employees.AnyAsync(e => e.PersonnelNumber == request.PersonnelNumber))
            return Conflict($"Personnel number '{request.PersonnelNumber}' already exists.");

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            PersonnelNumber = request.PersonnelNumber,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            TeamId = request.TeamId
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = employee.Id }, ToDto(employee));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateEmployeeRequest request)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is null)
            return NotFound();

        if (request.TeamId is { } teamId && !await db.Teams.AnyAsync(t => t.Id == teamId))
            return BadRequest($"Team '{teamId}' does not exist.");

        if (await db.Employees.AnyAsync(e => e.PersonnelNumber == request.PersonnelNumber && e.Id != id))
            return Conflict($"Personnel number '{request.PersonnelNumber}' already exists.");

        employee.PersonnelNumber = request.PersonnelNumber;
        employee.FirstName = request.FirstName;
        employee.LastName = request.LastName;
        employee.Email = request.Email;
        employee.Active = request.Active;
        employee.TeamId = request.TeamId;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is null)
            return NotFound();

        db.Employees.Remove(employee);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // "mögliche Schichten" (readme.md §3) — which shift types this employee may be
    // scheduled for. Enforced by ShiftPlanner.Application.Validation.EligibilityValidator.
    [HttpGet("{id:guid}/eligible-shift-types")]
    public async Task<ActionResult<IEnumerable<ShiftTypeDto>>> GetEligibleShiftTypes(Guid id)
    {
        var employee = await db.Employees.Include(e => e.EligibleShiftTypes).FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound();

        return Ok(employee.EligibleShiftTypes
            .OrderBy(s => s.StartTime)
            .Select(s => new ShiftTypeDto(s.Id, s.Name, s.StartTime, s.EndTime, s.BreakMinutes, s.Color, s.Active,
                s.MinStaffing, s.MaxStaffing)));
    }

    [HttpPut("{id:guid}/eligible-shift-types")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> SetEligibleShiftTypes(Guid id, List<Guid> shiftTypeIds)
    {
        var employee = await db.Employees.Include(e => e.EligibleShiftTypes).FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound();

        var shiftTypes = await db.ShiftTypes.Where(s => shiftTypeIds.Contains(s.Id)).ToListAsync();
        if (shiftTypes.Count != shiftTypeIds.Distinct().Count())
            return BadRequest("One or more shift type ids do not exist.");

        employee.EligibleShiftTypes = shiftTypes;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
