using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Application.Suggestions;
using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record ScheduleDto(
    Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ScheduleStatus Status,
    DateTimeOffset? PublishedAt, string? PublishedBy);

public record ShiftAssignmentDto(
    Guid Id, Guid ScheduleId, Guid EmployeeId, Guid ShiftTypeId,
    DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, TimeOnly? BreakStartTime,
    decimal NetHours, decimal? LaborCost);

public record ScheduleDetailDto(
    Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ScheduleStatus Status,
    DateTimeOffset? PublishedAt, string? PublishedBy,
    List<ShiftAssignmentDto> Assignments);

public record CreateScheduleRequest([Required, MaxLength(200)] string Name, DateOnly StartDate, DateOnly EndDate);

// issue #68: Status is no longer settable here — a Schedule only transitions Draft ->
// Published via POST .../publish (gated on zero blocking validation Errors) and Published ->
// Archived via POST .../archive. Name/StartDate/EndDate remain editable through this endpoint,
// but only while still Draft (see Update below).
public record UpdateScheduleRequest([Required, MaxLength(200)] string Name, DateOnly StartDate, DateOnly EndDate);

public record ShiftSuggestionDto(Guid EmployeeId, string FirstName, string LastName, bool Eligible, decimal Score, List<SuggestionReason> Reasons);

// issue #63: one row of the auto-fill dry-run preview — the manager reviews/trims these
// before POSTing the (possibly-trimmed) set back as an AutoFillCommitRequest.
public record AutoFillProposalDto(
    Guid EmployeeId, string FirstName, string LastName,
    Guid ShiftTypeId, string ShiftTypeName, DateOnly Date, decimal Score,
    List<SuggestionReason> Reasons);

public record AutoFillCommitItem(Guid EmployeeId, Guid ShiftTypeId, DateOnly Date);
public record AutoFillCommitRequest(List<AutoFillCommitItem> Assignments);

public record CopyMonthRequest(
    [Required, MaxLength(200)] string TargetName, DateOnly TargetStartDate, DateOnly TargetEndDate);

public record CopyMonthResponse(ScheduleDto Target, int CopiedCount);

public record CreateAssignmentRequest(
    Guid EmployeeId, Guid ShiftTypeId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes, TimeOnly? BreakStartTime = null);

public record UpdateAssignmentRequest(
    Guid EmployeeId, Guid ShiftTypeId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes, TimeOnly? BreakStartTime = null);

[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class SchedulesController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Schedule, ScheduleDto> ToDto =
        s => new ScheduleDto(s.Id, s.Name, s.StartDate, s.EndDate, s.Status, s.PublishedAt, s.PublishedBy);

    private static ShiftAssignmentDto ToAssignmentDto(ShiftAssignment a, decimal? hourlyRate, bool isHoliday)
    {
        var netHours = WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes);
        var laborCost = WageCalculator.LaborCost(a.StartTime, a.EndTime, a.Date.DayOfWeek, isHoliday, netHours, hourlyRate,
            a.BreakMinutes, a.BreakStartTime);
        return new(a.Id, a.ScheduleId, a.EmployeeId, a.ShiftTypeId, a.Date, a.StartTime, a.EndTime, a.BreakMinutes, a.BreakStartTime,
            netHours, laborCost);
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

        // issue #57: which nationwide-vs-Bundesland holiday set applies depends on each
        // assignment's employee's Team, so this resolves a HashSet per distinct Bundesland
        // actually in play (including null = nationwide-only) rather than one shared set.
        //
        // Bundesland? (null = nationwide-only) cannot be used as a Dictionary key directly:
        // Dictionary<TKey,TValue> throws ArgumentNullException on a null key at runtime even
        // when TKey is a nullable value type — CS8714 here was flagging a real bug, not just a
        // nullable-analysis false positive. HolidaysFor keeps a non-nullable Dictionary<Bundesland,
        // ...> for actual states plus a separate set for the nationwide/null case instead.
        var bundeslandByEmployee = await db.Employees.Include(e => e.Team)
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Team?.Bundesland);
        var nationwideHolidays = GermanPublicHolidays.InRange(schedule.StartDate, schedule.EndDate)
            .Select(h => h.Date).ToHashSet();
        var holidaysByBundesland = new Dictionary<Bundesland, HashSet<DateOnly>>();
        foreach (var land in bundeslandByEmployee.Values.Distinct())
        {
            if (land is { } b)
                holidaysByBundesland[b] = GermanPublicHolidays.InRange(schedule.StartDate, schedule.EndDate, b)
                    .Select(h => h.Date).ToHashSet();
        }
        HashSet<DateOnly> HolidaysFor(Bundesland? land) =>
            land is { } b ? holidaysByBundesland[b] : nationwideHolidays;

        return Ok(new ScheduleDetailDto(schedule.Id, schedule.Name, schedule.StartDate, schedule.EndDate,
            schedule.Status, schedule.PublishedAt, schedule.PublishedBy,
            assignments.Select(a => ToAssignmentDto(a, HourlyRateOn(contracts, a.EmployeeId, a.Date),
                HolidaysFor(bundeslandByEmployee.GetValueOrDefault(a.EmployeeId)).Contains(a.Date))).ToList()));
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

        // issue #68: a Published/Archived schedule's own span is what /publish's validation
        // was run against — changing it afterwards would silently invalidate that check.
        if (schedule.Status != ScheduleStatus.Draft)
            return Conflict($"Schedule is {schedule.Status}; only a Draft schedule's name/dates can be edited.");

        schedule.Name = request.Name;
        schedule.StartDate = request.StartDate;
        schedule.EndDate = request.EndDate;
        await db.SaveChangesAsync();

        return NoContent();
    }

    // issue #68: loads exactly the data ScheduleValidator needs for one Schedule — shared by
    // both the read-only /validate endpoint and the /publish use case's own blocking check, so
    // the two can never disagree about what counts as a blocking Error.
    private async Task<Application.Validation.ValidationResult> ValidateScheduleAsync(Schedule schedule)
    {
        var assignments = await db.ShiftAssignments.Where(a => a.ScheduleId == schedule.Id).ToListAsync();
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

        return ScheduleValidator.Validate(schedule, assignments, employees, shiftTypes, contracts, historyAssignments, absences);
    }

    private string CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name) ?? "system";

    // issue #68: the PublishSchedule use case — requires zero blocking Errors from the same
    // check /validate already exposes, so a manager can't publish (and thereby freeze) a
    // schedule that's still legally/operationally broken. Returns the full ValidationResult in
    // the 409 body when blocked, reusing the exact shape the validation panel already renders.
    [HttpPost("schedules/{id:guid}/publish")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<ScheduleDto>> Publish(Guid id)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        if (schedule.Status != ScheduleStatus.Draft)
            return Conflict($"Schedule is {schedule.Status}; only a Draft schedule can be published.");

        var result = await ValidateScheduleAsync(schedule);
        if (!result.IsValid)
            return Conflict(result);

        schedule.Status = ScheduleStatus.Published;
        schedule.PublishedAt = DateTimeOffset.UtcNow;
        schedule.PublishedBy = CurrentUserId();
        await db.SaveChangesAsync();

        return Ok(ToDto(schedule));
    }

    // issue #68: Published -> Archived. No validation re-check — a schedule already published
    // can be archived regardless of anything that changed operationally since (e.g. an employee
    // later deactivated), since Archived is a "this period is over" marker, not a quality gate.
    [HttpPost("schedules/{id:guid}/archive")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<ScheduleDto>> Archive(Guid id)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        if (schedule.Status != ScheduleStatus.Published)
            return Conflict($"Schedule is {schedule.Status}; only a Published schedule can be archived.");

        schedule.Status = ScheduleStatus.Archived;
        await db.SaveChangesAsync();

        return Ok(ToDto(schedule));
    }

    // issue #82: the frontend's "Monat kopieren" used to create each copied assignment via its
    // own POST /assignments call in a loop — a failure partway through left the target month
    // partially copied with no rollback. This computes the whole change set and applies it in
    // one SaveChangesAsync call, which EF Core already wraps in a single DB transaction, so a
    // failure rolls back everything instead of partially applying.
    [HttpPost("schedules/{id:guid}/copy")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<CopyMonthResponse>> CopyMonth(Guid id, CopyMonthRequest request)
    {
        var source = await db.Schedules.FindAsync(id);
        if (source is null)
            return NotFound();

        if (request.TargetEndDate < request.TargetStartDate)
            return BadRequest("TargetEndDate must not be before TargetStartDate.");

        var sourceAssignments = await db.ShiftAssignments.Where(a => a.ScheduleId == id).ToListAsync();

        var target = await db.Schedules.FirstOrDefaultAsync(s => s.StartDate == request.TargetStartDate);
        if (target is not null && target.Status != ScheduleStatus.Draft)
            return Conflict($"Target schedule is {target.Status}; copy aborted.");
        if (target is not null && await db.ShiftAssignments.AnyAsync(a => a.ScheduleId == target.Id))
            return Conflict("Target month already has assignments; copy aborted.");

        if (target is null)
        {
            target = new Schedule
            {
                Id = Guid.NewGuid(),
                Name = request.TargetName,
                StartDate = request.TargetStartDate,
                EndDate = request.TargetEndDate,
            };
            db.Schedules.Add(target);
        }

        // Same day-of-month next month; clamped into shorter months (e.g. 31 → 28/29/30).
        var daysInTargetMonth = DateTime.DaysInMonth(request.TargetStartDate.Year, request.TargetStartDate.Month);
        foreach (var a in sourceAssignments)
        {
            var day = Math.Min(a.Date.Day, daysInTargetMonth);
            db.ShiftAssignments.Add(new ShiftAssignment
            {
                Id = Guid.NewGuid(),
                ScheduleId = target.Id,
                EmployeeId = a.EmployeeId,
                ShiftTypeId = a.ShiftTypeId,
                Date = new DateOnly(request.TargetStartDate.Year, request.TargetStartDate.Month, day),
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                BreakMinutes = a.BreakMinutes,
                BreakStartTime = a.BreakStartTime,
            });
        }

        await db.SaveChangesAsync();

        return Ok(new CopyMonthResponse(ToDto(target), sourceAssignments.Count));
    }

    [HttpGet("schedules/{id:guid}/validate")]
    public async Task<ActionResult<Application.Validation.ValidationResult>> Validate(Guid id)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        return Ok(await ValidateScheduleAsync(schedule));
    }

    // readme.md §17's "Arbeitszeitpräferenzen" — ranks active employees for one open
    // (date, ShiftType) slot in this Schedule. See ShiftSuggestionEngine for the scoring.
    [HttpGet("schedules/{id:guid}/suggestions")]
    public async Task<ActionResult<IEnumerable<ShiftSuggestionDto>>> Suggest(Guid id, DateOnly date, Guid shiftTypeId)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();
        if (date < schedule.StartDate || date > schedule.EndDate)
            return BadRequest("Date is outside the schedule's range.");

        var shiftType = await db.ShiftTypes.FindAsync(shiftTypeId);
        if (shiftType is null)
            return NotFound();

        var employees = await db.Employees.Include(e => e.EligibleShiftTypes)
            .Where(e => e.Active).ToListAsync();
        var employeeIds = employees.Select(e => e.Id).ToList();

        // Same ±6-day lookback window as the /validate endpoint's historyAssignments, and the
        // same reason: rest-time/consecutive-day checks need shifts outside this Schedule too.
        var historyStart = date.AddDays(-6);
        var historyEnd = date.AddDays(6);
        var historyAssignments = await db.ShiftAssignments
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Date >= historyStart && a.Date <= historyEnd)
            .ToListAsync();

        var absences = await db.Absences
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.From <= date && a.To >= date)
            .ToListAsync();
        var scheduleAssignments = await db.ShiftAssignments.Where(a => a.ScheduleId == id).ToListAsync();
        var contracts = await db.Contracts.Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();
        var shiftTypePreferences = await db.ShiftTypePreferences.Where(p => employeeIds.Contains(p.EmployeeId)).ToListAsync();
        var weekdayPreferences = await db.WeekdayPreferences.Where(p => employeeIds.Contains(p.EmployeeId)).ToListAsync();

        var suggestions = ShiftSuggestionEngine.Suggest(
            date, shiftType, employees, historyAssignments, absences,
            schedule.StartDate, schedule.EndDate, scheduleAssignments, contracts,
            shiftTypePreferences, weekdayPreferences);

        var employeesById = employees.ToDictionary(e => e.Id);
        return Ok(suggestions.Select(s => new ShiftSuggestionDto(
            s.EmployeeId, employeesById[s.EmployeeId].FirstName, employeesById[s.EmployeeId].LastName,
            s.Eligible, s.Score, s.Reasons)));
    }

    // issue #63: bulk/auto-fill dry run — walks every open (date, ShiftType) slot in
    // [from, to] (defaulting to the whole Schedule) and returns the top-ranked eligible pick
    // per slot from ShiftSuggestionEngine.AutoFill, without persisting anything. The manager
    // reviews/trims this list client-side, then POSTs the kept rows to /auto-fill to commit.
    [HttpGet("schedules/{id:guid}/auto-fill-preview")]
    public async Task<ActionResult<IEnumerable<AutoFillProposalDto>>> AutoFillPreview(Guid id, DateOnly? from, DateOnly? to)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        var rangeStart = from ?? schedule.StartDate;
        var rangeEnd = to ?? schedule.EndDate;
        if (rangeStart < schedule.StartDate || rangeEnd > schedule.EndDate || rangeEnd < rangeStart)
            return BadRequest("Range must be a valid sub-range of the schedule's own date range.");

        var shiftTypes = await db.ShiftTypes.Where(s => s.Active).ToListAsync();
        var employees = await db.Employees.Include(e => e.EligibleShiftTypes)
            .Where(e => e.Active).ToListAsync();
        var employeeIds = employees.Select(e => e.Id).ToList();

        // Same ±6-day lookback window as /validate and /suggestions — rest-time/consecutive-day
        // checks need shifts outside the requested range too.
        var historyStart = rangeStart.AddDays(-6);
        var historyEnd = rangeEnd.AddDays(6);
        var historyAssignments = await db.ShiftAssignments
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Date >= historyStart && a.Date <= historyEnd)
            .ToListAsync();

        var absences = await db.Absences
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.From <= rangeEnd && a.To >= rangeStart)
            .ToListAsync();
        var scheduleAssignments = await db.ShiftAssignments.Where(a => a.ScheduleId == id).ToListAsync();
        var contracts = await db.Contracts.Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();
        var shiftTypePreferences = await db.ShiftTypePreferences.Where(p => employeeIds.Contains(p.EmployeeId)).ToListAsync();
        var weekdayPreferences = await db.WeekdayPreferences.Where(p => employeeIds.Contains(p.EmployeeId)).ToListAsync();

        var proposals = ShiftSuggestionEngine.AutoFill(
            rangeStart, rangeEnd, shiftTypes, employees, historyAssignments, absences,
            schedule.StartDate, schedule.EndDate, scheduleAssignments, contracts,
            shiftTypePreferences, weekdayPreferences);

        var employeesById = employees.ToDictionary(e => e.Id);
        var shiftTypesById = shiftTypes.ToDictionary(s => s.Id);
        return Ok(proposals.Select(p => new AutoFillProposalDto(
            p.EmployeeId, employeesById[p.EmployeeId].FirstName, employeesById[p.EmployeeId].LastName,
            p.ShiftTypeId, shiftTypesById[p.ShiftTypeId].Name, p.Date, p.Score, p.Reasons)));
    }

    // issue #63: commits a (possibly manager-trimmed) set of proposals from the preview above —
    // does not recompute them, so what the manager saw is exactly what gets written. Each item
    // is created the same way a single "Zuweisen" click already does (ShiftType template
    // times), so LaborCost/NetHours on the returned DTOs match CreateAssignment's shape.
    [HttpPost("schedules/{id:guid}/auto-fill")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<IEnumerable<ShiftAssignmentDto>>> AutoFillCommit(Guid id, AutoFillCommitRequest request)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        if (schedule.Status != ScheduleStatus.Draft)
            return Conflict($"Schedule is {schedule.Status}; assignments can only be committed to a Draft schedule.");

        if (request.Assignments.Count == 0)
            return Ok(Array.Empty<ShiftAssignmentDto>());

        var shiftTypesById = await db.ShiftTypes.ToDictionaryAsync(s => s.Id);
        // issue #104: pre-load every referenced EmployeeId in one query instead of an
        // AnyAsync roundtrip per item — same pre-load shape shiftTypesById already uses.
        var requestedEmployeeIds = request.Assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var existingEmployeeIds = await db.Employees
            .Where(e => requestedEmployeeIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToHashSetAsync();
        var created = new List<ShiftAssignment>();
        foreach (var item in request.Assignments)
        {
            if (item.Date < schedule.StartDate || item.Date > schedule.EndDate)
                return BadRequest($"Date '{item.Date}' is outside the schedule's range.");

            if (!existingEmployeeIds.Contains(item.EmployeeId))
                return BadRequest($"Employee '{item.EmployeeId}' does not exist.");

            if (!shiftTypesById.TryGetValue(item.ShiftTypeId, out var shiftType))
                return BadRequest($"Shift type '{item.ShiftTypeId}' does not exist.");

            created.Add(new ShiftAssignment
            {
                Id = Guid.NewGuid(),
                ScheduleId = id,
                EmployeeId = item.EmployeeId,
                ShiftTypeId = item.ShiftTypeId,
                Date = item.Date,
                StartTime = shiftType.StartTime,
                EndTime = shiftType.EndTime,
                BreakMinutes = shiftType.BreakMinutes,
            });
        }

        db.ShiftAssignments.AddRange(created);
        await db.SaveChangesAsync();

        var employeeIds = created.Select(a => a.EmployeeId).Distinct().ToList();
        var contracts = await db.Contracts.Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();
        var holidayDates = GermanPublicHolidays.InRange(schedule.StartDate, schedule.EndDate).Select(h => h.Date).ToHashSet();

        return Ok(created.Select(a => ToAssignmentDto(a, HourlyRateOn(contracts, a.EmployeeId, a.Date), holidayDates.Contains(a.Date))));
    }

    [HttpPost("schedules/{id:guid}/assignments")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<ShiftAssignmentDto>> CreateAssignment(Guid id, CreateAssignmentRequest request)
    {
        var schedule = await db.Schedules.FindAsync(id);
        if (schedule is null)
            return NotFound();

        // issue #68: writes are only allowed while the owning Schedule is still Draft, and an
        // assignment's Date must fall within the Schedule's own span — neither was enforced
        // server-side before, so a stale UI (or a direct API call) could silently corrupt an
        // already-published schedule or place a shift outside the period it belongs to.
        if (schedule.Status != ScheduleStatus.Draft)
            return Conflict($"Schedule is {schedule.Status}; assignments can only be added to a Draft schedule.");

        if (request.Date < schedule.StartDate || request.Date > schedule.EndDate)
            return BadRequest("Date is outside the schedule's range.");

        // issue #57: loaded (not just checked with AnyAsync) so its Team's Bundesland is on
        // hand below for the holiday/wage-surcharge lookup.
        var employee = await db.Employees.Include(e => e.Team).FirstOrDefaultAsync(e => e.Id == request.EmployeeId);
        if (employee is null)
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
            BreakMinutes = request.BreakMinutes,
            BreakStartTime = request.BreakStartTime
        };
        db.ShiftAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var contract = await db.Contracts
            .Where(c => c.EmployeeId == assignment.EmployeeId && c.ValidFrom <= assignment.Date
                && (c.ValidTo == null || c.ValidTo >= assignment.Date))
            .OrderByDescending(c => c.ValidFrom)
            .FirstOrDefaultAsync();

        var isHoliday = GermanPublicHolidays.InRange(assignment.Date, assignment.Date, employee.Team?.Bundesland).Count > 0;
        return CreatedAtAction(nameof(GetById), new { id }, ToAssignmentDto(assignment, contract?.HourlyRate, isHoliday));
    }

    [HttpPut("assignments/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> UpdateAssignment(Guid id, UpdateAssignmentRequest request)
    {
        var assignment = await db.ShiftAssignments.FindAsync(id);
        if (assignment is null)
            return NotFound();

        var schedule = await db.Schedules.FindAsync(assignment.ScheduleId);
        if (schedule is null)
            return NotFound();

        if (schedule.Status != ScheduleStatus.Draft)
            return Conflict($"Schedule is {schedule.Status}; assignments can only be edited on a Draft schedule.");

        if (request.Date < schedule.StartDate || request.Date > schedule.EndDate)
            return BadRequest("Date is outside the schedule's range.");

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
        assignment.BreakStartTime = request.BreakStartTime;
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

        var schedule = await db.Schedules.FindAsync(assignment.ScheduleId);
        if (schedule is null)
            return NotFound();

        if (schedule.Status != ScheduleStatus.Draft)
            return Conflict($"Schedule is {schedule.Status}; assignments can only be deleted from a Draft schedule.");

        db.ShiftAssignments.Remove(assignment);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
