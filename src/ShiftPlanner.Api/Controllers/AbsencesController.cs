using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record AbsenceDto(Guid Id, Guid EmployeeId, DateOnly From, DateOnly To, AbsenceType Type, string? Comment);

public record CreateAbsenceRequest(DateOnly From, DateOnly To, AbsenceType Type, [MaxLength(1000)] string? Comment);

public record UpdateAbsenceRequest(DateOnly From, DateOnly To, AbsenceType Type, [MaxLength(1000)] string? Comment);

// readme.md §8/issue #17: same policy split as ContractsController — Absence is employee data.
[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class AbsencesController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Absence, AbsenceDto> ToDto =
        a => new AbsenceDto(a.Id, a.EmployeeId, a.From, a.To, a.Type, a.Comment);

    [HttpGet("employees/{employeeId:guid}/absences")]
    public async Task<ActionResult<IEnumerable<AbsenceDto>>> GetForEmployee(Guid employeeId)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId))
            return NotFound();

        var absences = await db.Absences
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.From)
            .ToListAsync();
        return Ok(absences.Select(ToDto));
    }

    [HttpGet("absences/{id:guid}")]
    public async Task<ActionResult<AbsenceDto>> GetById(Guid id)
    {
        var absence = await db.Absences.FindAsync(id);
        return absence is null ? NotFound() : Ok(ToDto(absence));
    }

    [HttpPost("employees/{employeeId:guid}/absences")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<AbsenceDto>> Create(Guid employeeId, CreateAbsenceRequest request)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId))
            return NotFound();

        if (request.To < request.From)
            return BadRequest("'To' must not be before 'From'.");

        var absence = new Absence
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            From = request.From,
            To = request.To,
            Type = request.Type,
            Comment = request.Comment
        };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = absence.Id }, ToDto(absence));
    }

    [HttpPut("absences/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateAbsenceRequest request)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
            return NotFound();

        if (request.To < request.From)
            return BadRequest("'To' must not be before 'From'.");

        absence.From = request.From;
        absence.To = request.To;
        absence.Type = request.Type;
        absence.Comment = request.Comment;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("absences/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
            return NotFound();

        db.Absences.Remove(absence);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
