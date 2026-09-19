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

public record HoursReportShiftDto(Guid ShiftTypeId, TimeOnly StartTime, TimeOnly EndTime, decimal NetHours, bool EndsNextDay);

public record HoursReportDayDto(
    DateOnly Date, List<HoursReportShiftDto> Shifts, decimal NetHours, AbsenceType? Absence, bool IsHoliday);

public record HoursReportDto(
    Guid EmployeeId, DateOnly From, DateOnly To,
    decimal SollHours, decimal IstHours, decimal Deviation,
    decimal BalanceBefore, decimal BalanceAfter,
    List<HoursReportDayDto> Days, List<HoursAdjustmentDto> Adjustments);

public record ShiftTypePreferenceDto(Guid ShiftTypeId, PreferenceLevel Level);
public record WeekdayPreferenceDto(DayOfWeek DayOfWeek, PreferenceLevel Level);
public record SetEligibleShiftTypesRequest(List<Guid> ShiftTypeIds);

[ApiController]
[Route("api/employees")]
[Authorize(Policy = "ApiRead")]
public class EmployeesController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Employee, EmployeeDto> ToDto =
        e => new EmployeeDto(e.Id, e.PersonnelNumber, e.FirstName, e.LastName, e.Email, e.PhoneNumber, e.Active, e.TeamId);

    [HttpGet]
    // issue #110: optional skip/take, defaulting to unbounded (unchanged behavior) when omitted
    // so existing callers that fetch the whole list to filter/search client-side keep working.
    public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll(int? skip, int? take)
    {
        if (skip < 0 || take < 0)
            return BadRequest("'skip'/'take' must not be negative.");

        IQueryable<Employee> query = db.Employees
            .AsNoTracking()
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName);
        if (skip is not null) query = query.Skip(skip.Value);
        if (take is not null) query = query.Take(take.Value);

        var employees = await query.ToListAsync();
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
        var adjustments = await db.HoursAdjustments.AsNoTracking()
            .Where(a => a.EmployeeId == id && a.Date < cutoff).ToListAsync();

        return Ok(HoursBalanceCalculator.CumulativeBalance(id, cutoff, schedules, assignments, contracts, absences, adjustments));
    }

    // issue #165: the printable "sign this" monthly report — day-by-day Ist hours for [from,to],
    // the period's Soll, the deviation, any admin HoursAdjustments in range (with their Reason),
    // and the running balance carried in/out of the period (same CumulativeBalance math the
    // Übertrag readout already uses, just exposed with both endpoints instead of one figure).
    [HttpGet("{id:guid}/hours-report")]
    public async Task<ActionResult<HoursReportDto>> HoursReport(Guid id, DateOnly from, DateOnly to)
    {
        var employee = await db.Employees.AsNoTracking().Include(e => e.Team).FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound();
        if (to < from)
            return BadRequest("'to' must not be before 'from'.");

        var assignments = await db.ShiftAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == id && a.Date >= from && a.Date <= to)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.EmployeeId == id).ToListAsync();
        var absences = await db.Absences.AsNoTracking().Where(a => a.EmployeeId == id).ToListAsync();
        var adjustmentsInRange = await db.HoursAdjustments.AsNoTracking()
            .Where(a => a.EmployeeId == id && a.Date >= from && a.Date <= to)
            .OrderBy(a => a.Date).ToListAsync();
        var priorAdjustments = await db.HoursAdjustments.AsNoTracking()
            .Where(a => a.EmployeeId == id && a.Date < from).ToListAsync();

        var priorSchedules = await db.Schedules.AsNoTracking().Where(s => s.EndDate < from).ToListAsync();
        var priorScheduleIds = priorSchedules.Select(s => s.Id).ToHashSet();
        var priorAssignments = await db.ShiftAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == id && priorScheduleIds.Contains(a.ScheduleId)).ToListAsync();

        var holidays = GermanPublicHolidays.InRange(from, to, employee.Team?.Bundesland)
            .Select(h => h.Date).ToHashSet();

        var days = new List<HoursReportDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayAssignments = assignments.Where(a => a.Date == date).ToList();
            var absence = absences.FirstOrDefault(a => a.From <= date && a.To >= date);
            days.Add(new HoursReportDayDto(
                date,
                dayAssignments.Select(a => new HoursReportShiftDto(
                    a.ShiftTypeId, a.StartTime, a.EndTime,
                    WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes, a.EndsNextDay),
                    a.EndsNextDay)).ToList(),
                dayAssignments.Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes, a.EndsNextDay)),
                absence?.Type,
                holidays.Contains(date)));
        }

        var sollHours = WorkingTimeCalculator.ExpectedHours(contracts, absences, id, from, to);
        var istHours = days.Sum(d => d.NetHours);
        var balanceBefore = HoursBalanceCalculator.CumulativeBalance(
            id, from, priorSchedules, priorAssignments, contracts, absences, priorAdjustments);
        var balanceAfter = balanceBefore + (istHours - sollHours)
            + adjustmentsInRange.Sum(a => a.HoursDelta);

        var dto = new HoursReportDto(
            employee.Id, from, to, sollHours, istHours, istHours - sollHours,
            balanceBefore, balanceAfter, days,
            adjustmentsInRange.Select(a => new HoursAdjustmentDto(
                a.Id, a.EmployeeId, a.Date, a.HoursDelta, a.Reason, a.CreatedBy, a.CreatedAt)).ToList());
        return Ok(dto);
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

    // `deleteAssignments=true` is the escape hatch for data nobody wants to keep (test/demo
    // employees, mainly) — it deletes the employee's ShiftAssignments right along with them
    // instead of stopping at the 409 below. Defaults to false so the safe path (the 409 telling
    // the caller to remove shifts first or deactivate instead) stays the default for every
    // existing caller; the frontend only ever sends true as an explicit second confirmation
    // after the user has already seen and dismissed that 409 once.
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Delete(Guid id, bool deleteAssignments = false)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is null)
            return NotFound();

        // ShiftAssignment.EmployeeId is a Restrict FK (ApplicationDbContext) — deleting an
        // employee who still has shifts assigned would otherwise fail at the database level
        // with an opaque foreign-key-violation 500 (caught generically by
        // GlobalExceptionMiddleware, but with no way to say *why* or what to do about it).
        // Checked explicitly here instead so the manager gets a specific, actionable reason.
        var assignments = await db.ShiftAssignments.Where(a => a.EmployeeId == id).ToListAsync();
        if (assignments.Count > 0 && !deleteAssignments)
            return Conflict(
                $"Mitarbeiter '{employee.FirstName} {employee.LastName}' kann nicht gelöscht werden: " +
                (assignments.Count == 1
                    ? "es ist noch 1 Schicht"
                    : $"es sind noch {assignments.Count} Schichten") +
                " im Dienstplan zugewiesen. Bitte entfernen Sie zuerst die betroffenen Schichten, " +
                "oder deaktivieren Sie den Mitarbeiter stattdessen über das Feld „Aktiv“, um die " +
                "Historie zu erhalten.");

        // Deletes in the same SaveChangesAsync call as the Employee itself below, so EF Core's
        // single implicit transaction covers both — either everything goes, or (on any failure)
        // nothing does, same atomicity every other multi-row write in this codebase relies on
        // (e.g. issue #82's /copy). AuditSaveChangesInterceptor still logs every one of these
        // Deletes individually, so the "no history kept" the caller asked for is about the
        // Dienstplan, not about losing the audit trail of what was deleted and by whom.
        if (assignments.Count > 0)
            db.ShiftAssignments.RemoveRange(assignments);

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
                s.MinStaffing, s.MaxStaffing, s.EndsNextDay)));
    }

    [HttpPut("{id:guid}/eligible-shift-types")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> SetEligibleShiftTypes(Guid id, SetEligibleShiftTypesRequest request)
    {
        var shiftTypeIds = request.ShiftTypeIds;
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
