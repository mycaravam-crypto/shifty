using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record TeamDto(Guid Id, string Name, bool Active);

public record CreateTeamRequest([Required, MaxLength(200)] string Name);

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
            .Select(t => new TeamDto(t.Id, t.Name, t.Active))
            .ToListAsync();
        return Ok(teams);
    }

    [HttpPost]
    [Authorize(Policy = "ApiWrite")]
    public async Task<ActionResult<TeamDto>> Create(CreateTeamRequest request)
    {
        if (await db.Teams.AnyAsync(t => t.Name == request.Name))
            return Conflict($"Team '{request.Name}' already exists.");

        var team = new Team { Id = Guid.NewGuid(), Name = request.Name };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var dto = new TeamDto(team.Id, team.Name, team.Active);
        return CreatedAtAction(nameof(GetAll), new { id = team.Id }, dto);
    }
}
