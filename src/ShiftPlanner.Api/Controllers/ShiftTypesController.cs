using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record ShiftTypeDto(
    Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, string Color, bool Active,
    int? MinStaffing, int? MaxStaffing);

public record CreateShiftTypeRequest(
    [Required, MaxLength(100)] string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes,
    [Required] string Color,
    [Range(1, 1000)] int? MinStaffing = null,
    [Range(1, 1000)] int? MaxStaffing = null);

public record UpdateShiftTypeRequest(
    [Required, MaxLength(100)] string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    [Range(0, 480)] int BreakMinutes,
    [Required] string Color,
    bool Active,
    [Range(1, 1000)] int? MinStaffing = null,
    [Range(1, 1000)] int? MaxStaffing = null);

[ApiController]
[Route("api/shift-types")]
[Authorize(Policy = "ApiRead")]
public class ShiftTypesController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShiftTypeDto>>> GetAll()
    {
        var shiftTypes = await db.ShiftTypes
            .OrderBy(s => s.StartTime)
            .Select(s => new ShiftTypeDto(s.Id, s.Name, s.StartTime, s.EndTime, s.BreakMinutes, s.Color, s.Active,
                s.MinStaffing, s.MaxStaffing))
            .ToListAsync();
        return Ok(shiftTypes);
    }

    [HttpPost]
    [Authorize(Policy = "AdminWrite")]
    public async Task<ActionResult<ShiftTypeDto>> Create(CreateShiftTypeRequest request)
    {
        if (await db.ShiftTypes.AnyAsync(s => s.Name == request.Name))
            return Conflict($"Shift type '{request.Name}' already exists.");

        var shiftType = new ShiftType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            Color = request.Color,
            MinStaffing = request.MinStaffing,
            MaxStaffing = request.MaxStaffing
        };
        db.ShiftTypes.Add(shiftType);
        await db.SaveChangesAsync();

        var dto = new ShiftTypeDto(shiftType.Id, shiftType.Name, shiftType.StartTime, shiftType.EndTime, shiftType.BreakMinutes, shiftType.Color, shiftType.Active,
            shiftType.MinStaffing, shiftType.MaxStaffing);
        return CreatedAtAction(nameof(GetAll), new { id = shiftType.Id }, dto);
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

        shiftType.Name = request.Name;
        shiftType.StartTime = request.StartTime;
        shiftType.EndTime = request.EndTime;
        shiftType.BreakMinutes = request.BreakMinutes;
        shiftType.Color = request.Color;
        shiftType.Active = request.Active;
        shiftType.MinStaffing = request.MinStaffing;
        shiftType.MaxStaffing = request.MaxStaffing;
        await db.SaveChangesAsync();

        return NoContent();
    }
}
