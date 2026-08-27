using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record ShiftTypeDto(
    Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, string Color, bool Active,
    int? MinStaffing, int? MaxStaffing, bool EndsNextDay);

public record CreateShiftTypeRequest(
    [Required, MaxLength(100)] string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes,
    [Required] string Color,
    [Range(1, 1000)] int? MinStaffing = null,
    [Range(1, 1000)] int? MaxStaffing = null,
    bool EndsNextDay = false);

public record UpdateShiftTypeRequest(
    [Required, MaxLength(100)] string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes,
    [Required] string Color,
    bool Active,
    [Range(1, 1000)] int? MinStaffing = null,
    [Range(1, 1000)] int? MaxStaffing = null,
    bool EndsNextDay = false);

[ApiController]
[Route("api/shift-types")]
[Authorize(Policy = "ApiRead")]
public class ShiftTypesController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    // issue #110: optional skip/take, defaulting to unbounded (unchanged behavior) when omitted
    // so existing callers that fetch the whole list to filter/search client-side keep working.
    public async Task<ActionResult<IEnumerable<ShiftTypeDto>>> GetAll(int? skip, int? take)
    {
        if (skip < 0 || take < 0)
            return BadRequest("'skip'/'take' must not be negative.");

        IQueryable<ShiftTypeDto> query = db.ShiftTypes
            .AsNoTracking()
            .OrderBy(s => s.StartTime)
            .Select(s => new ShiftTypeDto(s.Id, s.Name, s.StartTime, s.EndTime, s.BreakMinutes, s.Color, s.Active,
                s.MinStaffing, s.MaxStaffing, s.EndsNextDay));
        if (skip is not null) query = query.Skip(skip.Value);
        if (take is not null) query = query.Take(take.Value);

        var shiftTypes = await query.ToListAsync();
        return Ok(shiftTypes);
    }

    [HttpPost]
    [Authorize(Policy = "AdminWrite")]
    public async Task<ActionResult<ShiftTypeDto>> Create(CreateShiftTypeRequest request)
    {
        if (await db.ShiftTypes.AnyAsync(s => s.Name == request.Name))
            return Conflict($"Shift type '{request.Name}' already exists.");

        if (!WorkingTimeCalculator.IsValidShiftTiming(request.StartTime, request.EndTime, request.EndsNextDay))
            return BadRequest("EndTime must be after StartTime, or strictly before it with EndsNextDay set (issue #157).");

        var shiftType = new ShiftType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            Color = request.Color,
            MinStaffing = request.MinStaffing,
            MaxStaffing = request.MaxStaffing,
            EndsNextDay = request.EndsNextDay
        };
        db.ShiftTypes.Add(shiftType);
        await db.SaveChangesAsync();

        var dto = new ShiftTypeDto(shiftType.Id, shiftType.Name, shiftType.StartTime, shiftType.EndTime, shiftType.BreakMinutes, shiftType.Color, shiftType.Active,
            shiftType.MinStaffing, shiftType.MaxStaffing, shiftType.EndsNextDay);
        // No GetById endpoint exists for this resource, so CreatedAtAction(nameof(GetAll), ...)
        // would produce a Location header pointing at the collection, not the created resource
        // (GetAll takes no id parameter) — StatusCode(201, ...) avoids that misleading header.
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateShiftTypeRequest request)
    {
        var shiftType = await db.ShiftTypes.FindAsync(id);
        if (shiftType is null)
            return NotFound();

        if (await db.ShiftTypes.AnyAsync(s => s.Name == request.Name && s.Id != id))
            return Conflict($"Shift type '{request.Name}' already exists.");

        if (!WorkingTimeCalculator.IsValidShiftTiming(request.StartTime, request.EndTime, request.EndsNextDay))
            return BadRequest("EndTime must be after StartTime, or strictly before it with EndsNextDay set (issue #157).");

        shiftType.Name = request.Name;
        shiftType.StartTime = request.StartTime;
        shiftType.EndTime = request.EndTime;
        shiftType.BreakMinutes = request.BreakMinutes;
        shiftType.Color = request.Color;
        shiftType.Active = request.Active;
        shiftType.MinStaffing = request.MinStaffing;
        shiftType.MaxStaffing = request.MaxStaffing;
        shiftType.EndsNextDay = request.EndsNextDay;
        await db.SaveChangesAsync();

        return NoContent();
    }
}
