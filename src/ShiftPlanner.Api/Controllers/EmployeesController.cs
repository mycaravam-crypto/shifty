using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record EmployeeDto(Guid Id, string PersonnelNumber, string FirstName, string LastName, string? Email, string? PhoneNumber, bool Active, Guid? TeamId);

public record CreateEmployeeRequest(
    [Required, MaxLength(50)] string PersonnelNumber,
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [EmailAddress] string? Email,
    [MaxLength(50)] string? PhoneNumber,
    Guid? TeamId);

public record UpdateEmployeeRequest(
    [Required, MaxLength(50)] string PersonnelNumber,
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [EmailAddress] string? Email,
    [MaxLength(50)] string? PhoneNumber,
    bool Active,
    Guid? TeamId);

public record ShiftTypePreferenceDto(Guid ShiftTypeId, PreferenceLevel Level);
public record WeekdayPreferenceDto(DayOfWeek DayOfWeek, PreferenceLevel Level);

[ApiController]
[Route("api/employees")]
[Authorize(Policy = "ApiRead")]
public class EmployeesController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Employee, EmployeeDto> ToDto =
        e => new EmployeeDto(e.Id, e.PersonnelNumber, e.FirstName, e.LastName, e.Email, e.PhoneNumber, e.Active, e.TeamId);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll()
    {
        var employees = await db.Employees
            .AsNoTracking()
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync();
        return Ok(employees.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> GetById(Guid id)
    {
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
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
        var schedules = await db.Schedules.AsNoTracking().Where(s => s.EndDate < cutoff).ToListAsync();
        var scheduleIds = schedules.Select(s => s.Id).ToList();
        var assignments = await db.ShiftAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == id && scheduleIds.Contains(a.ScheduleId)).ToListAsync();
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.EmployeeId == id).ToListAsync();
        var absences = await db.Absences.AsNoTracking().Where(a => a.EmployeeId == id).ToListAsync();

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
            PhoneNumber = request.PhoneNumber,
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
        employee.PhoneNumber = request.PhoneNumber;
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
        var employee = await db.Employees.AsNoTracking().Include(e => e.EligibleShiftTypes).FirstOrDefaultAsync(e => e.Id == id);
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

        employee.ReplaceEligibleShiftTypes(shiftTypes);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // readme.md §17's "Arbeitszeitpräferenzen" — which ShiftTypes this employee prefers or
    // would rather avoid (distinct from EligibleShiftTypes, which is "allowed" not "wanted").
    // Feeds ShiftSuggestionEngine. Full-replace PUT, same shape as eligible-shift-types above.
    [HttpGet("{id:guid}/shift-type-preferences")]
    public async Task<ActionResult<IEnumerable<ShiftTypePreferenceDto>>> GetShiftTypePreferences(Guid id)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id))
            return NotFound();

        var preferences = await db.ShiftTypePreferences.AsNoTracking().Where(p => p.EmployeeId == id).ToListAsync();
        return Ok(preferences.Select(p => new ShiftTypePreferenceDto(p.ShiftTypeId, p.Level)));
    }

    [HttpPut("{id:guid}/shift-type-preferences")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> SetShiftTypePreferences(Guid id, List<ShiftTypePreferenceDto> preferences)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id))
            return NotFound();

        var shiftTypeIds = preferences.Select(p => p.ShiftTypeId).ToList();
        if (await db.ShiftTypes.CountAsync(s => shiftTypeIds.Contains(s.Id)) != shiftTypeIds.Distinct().Count())
            return BadRequest("One or more shift type ids do not exist.");

        var existing = await db.ShiftTypePreferences.Where(p => p.EmployeeId == id).ToListAsync();
        db.ShiftTypePreferences.RemoveRange(existing);
        db.ShiftTypePreferences.AddRange(preferences.Select(p => new ShiftTypePreference
        {
            Id = Guid.NewGuid(),
            EmployeeId = id,
            ShiftTypeId = p.ShiftTypeId,
            Level = p.Level
        }));
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Which weekdays this employee prefers or would rather avoid working. Feeds
    // ShiftSuggestionEngine, same full-replace pattern as shift-type-preferences above.
    [HttpGet("{id:guid}/weekday-preferences")]
    public async Task<ActionResult<IEnumerable<WeekdayPreferenceDto>>> GetWeekdayPreferences(Guid id)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id))
            return NotFound();

        var preferences = await db.WeekdayPreferences.AsNoTracking().Where(p => p.EmployeeId == id).ToListAsync();
        return Ok(preferences.Select(p => new WeekdayPreferenceDto(p.DayOfWeek, p.Level)));
    }

    [HttpPut("{id:guid}/weekday-preferences")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> SetWeekdayPreferences(Guid id, List<WeekdayPreferenceDto> preferences)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id))
            return NotFound();

        if (preferences.Select(p => p.DayOfWeek).Distinct().Count() != preferences.Count)
            return BadRequest("Duplicate weekday in preferences.");

        var existing = await db.WeekdayPreferences.Where(p => p.EmployeeId == id).ToListAsync();
        db.WeekdayPreferences.RemoveRange(existing);
        db.WeekdayPreferences.AddRange(preferences.Select(p => new WeekdayPreference
        {
            Id = Guid.NewGuid(),
            EmployeeId = id,
            DayOfWeek = p.DayOfWeek,
            Level = p.Level
        }));
        await db.SaveChangesAsync();
        return NoContent();
    }
}
