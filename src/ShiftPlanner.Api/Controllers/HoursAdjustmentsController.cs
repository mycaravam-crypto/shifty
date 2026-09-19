using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record HoursAdjustmentDto(
    Guid Id, Guid EmployeeId, DateOnly Date, decimal HoursDelta, string Reason, string CreatedBy, DateTimeOffset CreatedAt);

public record CreateHoursAdjustmentRequest(DateOnly Date, decimal HoursDelta, [Required, MaxLength(1000)] string Reason);

public record UpdateHoursAdjustmentRequest(DateOnly Date, decimal HoursDelta, [Required, MaxLength(1000)] string Reason);

// issue #165: a manual correction to an employee's Ist/Soll balance, always carrying a reason —
// see HoursAdjustment. Gated by AdminWrite rather than ManagerWrite (the policy Absence/Contract
// use): this overrides a computed balance rather than recording routine Mitarbeiter data, so it's
// scoped to the stricter "admin function" the issue itself asked for.
[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class HoursAdjustmentsController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<HoursAdjustment, HoursAdjustmentDto> ToDto =
        a => new HoursAdjustmentDto(a.Id, a.EmployeeId, a.Date, a.HoursDelta, a.Reason, a.CreatedBy, a.CreatedAt);

    private string CurrentActor() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(ClaimTypes.Name)
        ?? "system";

    [HttpGet("employees/{employeeId:guid}/hours-adjustments")]
    public async Task<ActionResult<IEnumerable<HoursAdjustmentDto>>> GetForEmployee(Guid employeeId)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId))
            return NotFound();

        var adjustments = await db.HoursAdjustments
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.Date)
            .ToListAsync();
        return Ok(adjustments.Select(ToDto));
    }

    [HttpGet("hours-adjustments/{id:guid}")]
    public async Task<ActionResult<HoursAdjustmentDto>> GetById(Guid id)
    {
        var adjustment = await db.HoursAdjustments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        return adjustment is null ? NotFound() : Ok(ToDto(adjustment));
    }

    [HttpPost("employees/{employeeId:guid}/hours-adjustments")]
    [Authorize(Policy = "AdminWrite")]
    public async Task<ActionResult<HoursAdjustmentDto>> Create(Guid employeeId, CreateHoursAdjustmentRequest request)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId))
            return NotFound();

        HoursAdjustment adjustment;
        try
        {
            adjustment = HoursAdjustment.Create(
                Guid.NewGuid(), employeeId, request.Date, request.HoursDelta, request.Reason,
                CurrentActor(), DateTimeOffset.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        db.HoursAdjustments.Add(adjustment);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = adjustment.Id }, ToDto(adjustment));
    }

    [HttpPut("hours-adjustments/{id:guid}")]
    [Authorize(Policy = "AdminWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateHoursAdjustmentRequest request)
    {
        var adjustment = await db.HoursAdjustments.FindAsync(id);
        if (adjustment is null)
            return NotFound();

        adjustment.Date = request.Date;
        adjustment.HoursDelta = request.HoursDelta;
        adjustment.Reason = request.Reason;

        try
        {
            adjustment.Validate();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("hours-adjustments/{id:guid}")]
    [Authorize(Policy = "AdminWrite")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var adjustment = await db.HoursAdjustments.FindAsync(id);
        if (adjustment is null)
            return NotFound();

        db.HoursAdjustments.Remove(adjustment);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
