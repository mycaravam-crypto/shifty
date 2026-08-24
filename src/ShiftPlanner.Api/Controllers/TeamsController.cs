using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record TeamDto(Guid Id, string Name, bool Active, Bundesland? Bundesland);

// issue #57: Bundesland is optional (null = nationwide-only, matching every Team's behavior
// before this field existed).
public record CreateTeamRequest([Required, MaxLength(200)] string Name, Bundesland? Bundesland);

[ApiController]
[Route("api/teams")]
[Authorize(Policy = "ApiRead")]
public class TeamsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeamDto>>> GetAll()
    {
        var teams = await db.Teams
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(t.Id, t.Name, t.Active, t.Bundesland))
            .ToListAsync();
        return Ok(teams);
    }

    [HttpPost]
    [Authorize(Policy = "AdminWrite")]
    public async Task<ActionResult<TeamDto>> Create(CreateTeamRequest request)
    {
        if (await db.Teams.AnyAsync(t => t.Name == request.Name))
            return Conflict($"Team '{request.Name}' already exists.");

        var team = new Team { Id = Guid.NewGuid(), Name = request.Name, Bundesland = request.Bundesland };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var dto = new TeamDto(team.Id, team.Name, team.Active, team.Bundesland);
        // No GetById endpoint exists for this resource, so CreatedAtAction(nameof(GetAll), ...)
        // would produce a Location header pointing at the collection, not the created resource
        // (GetAll takes no id parameter) — StatusCode(201, ...) avoids that misleading header.
        return StatusCode(StatusCodes.Status201Created, dto);
    }
}
