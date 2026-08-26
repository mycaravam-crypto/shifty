using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

// issue #69: CRUD for the weekly staffing-demand pattern StaffingValidator/DashboardController
// now check directly, independent of whether anyone has actually been scheduled yet. Same
// policy split as TeamsController/ShiftTypesController — this is Stammdaten/planning
// configuration, not day-to-day Mitarbeiter/Planung data (readme.md §23's Admin-covers-
// Stammdaten split), so it's AdminWrite, not ManagerWrite.
public record StaffingRequirementDto(Guid Id, Guid? TeamId, Guid ShiftTypeId, DayOfWeek DayOfWeek, int MinimumStaffing);

public record CreateStaffingRequirementRequest(Guid? TeamId, Guid ShiftTypeId, DayOfWeek DayOfWeek, [Range(1, 1000)] int MinimumStaffing);

public record UpdateStaffingRequirementRequest(Guid? TeamId, Guid ShiftTypeId, DayOfWeek DayOfWeek, [Range(1, 1000)] int MinimumStaffing);

[ApiController]
[Route("api/staffing-requirements")]
[Authorize(Policy = "ApiRead")]
public class StaffingRequirementsController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<StaffingRequirement, StaffingRequirementDto> ToDto =
        r => new StaffingRequirementDto(r.Id, r.TeamId, r.ShiftTypeId, r.DayOfWeek, r.MinimumStaffing);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffingRequirementDto>>> GetAll()
    {
        var requirements = await db.StaffingRequirements
            .OrderBy(r => r.DayOfWeek).ThenBy(r => r.ShiftTypeId)
            .ToListAsync();
        return Ok(requirements.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StaffingRequirementDto>> GetById(Guid id)
    {
        var requirement = await db.StaffingRequirements.FindAsync(id);
        return requirement is null ? NotFound() : Ok(ToDto(requirement));
    }

    [HttpPost]
    [Authorize(Policy = "AdminWrite")]
    public async Task<ActionResult<StaffingRequirementDto>> Create(CreateStaffingRequirementRequest request)
    {
        if (!await db.ShiftTypes.AnyAsync(s => s.Id == request.ShiftTypeId))
            return BadRequest("Unknown ShiftTypeId.");
        if (request.TeamId is { } teamId && !await db.Teams.AnyAsync(t => t.Id == teamId))
            return BadRequest("Unknown TeamId.");

        if (await db.StaffingRequirements.AnyAsync(r =>
                r.TeamId == request.TeamId && r.ShiftTypeId == request.ShiftTypeId && r.DayOfWeek == request.DayOfWeek))
            return Conflict("A staffing requirement for this Team/ShiftType/DayOfWeek combination already exists.");

        var requirement = new StaffingRequirement
        {
            Id = Guid.NewGuid(),
            TeamId = request.TeamId,
            ShiftTypeId = request.ShiftTypeId,
            DayOfWeek = request.DayOfWeek,
            MinimumStaffing = request.MinimumStaffing
        };
        db.StaffingRequirements.Add(requirement);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = requirement.Id }, ToDto(requirement));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateStaffingRequirementRequest request)
    {
        var requirement = await db.StaffingRequirements.FindAsync(id);
        if (requirement is null)
            return NotFound();

        if (!await db.ShiftTypes.AnyAsync(s => s.Id == request.ShiftTypeId))
            return BadRequest("Unknown ShiftTypeId.");
        if (request.TeamId is { } teamId && !await db.Teams.AnyAsync(t => t.Id == teamId))
            return BadRequest("Unknown TeamId.");

        if (await db.StaffingRequirements.AnyAsync(r =>
                r.Id != id && r.TeamId == request.TeamId && r.ShiftTypeId == request.ShiftTypeId && r.DayOfWeek == request.DayOfWeek))
            return Conflict("A staffing requirement for this Team/ShiftType/DayOfWeek combination already exists.");

        requirement.TeamId = request.TeamId;
        requirement.ShiftTypeId = request.ShiftTypeId;
        requirement.DayOfWeek = request.DayOfWeek;
        requirement.MinimumStaffing = request.MinimumStaffing;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminWrite")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var requirement = await db.StaffingRequirements.FindAsync(id);
        if (requirement is null)
            return NotFound();

        db.StaffingRequirements.Remove(requirement);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
