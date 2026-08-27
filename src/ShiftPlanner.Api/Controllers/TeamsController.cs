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
    // issue #110: optional skip/take, defaulting to unbounded (unchanged behavior) when omitted
    // so existing callers that fetch the whole list to filter/search client-side keep working.
    public async Task<ActionResult<IEnumerable<TeamDto>>> GetAll(int? skip, int? take)
    {
        if (skip < 0 || take < 0)
            return BadRequest("'skip'/'take' must not be negative.");

        IQueryable<TeamDto> query = db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(t.Id, t.Name, t.Active, t.Bundesland));
        if (skip is not null) query = query.Skip(skip.Value);
        if (take is not null) query = query.Take(take.Value);

        var teams = await query.ToListAsync();
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
