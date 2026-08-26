using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record ContractDto(
    Guid Id, Guid EmployeeId, DateOnly ValidFrom, DateOnly? ValidTo, decimal WeeklyHours,
    int WorkingDaysPerWeek, decimal DailyTargetHours, decimal? HourlyRate);

public record CreateContractRequest(
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    [Range(0, 168)] decimal WeeklyHours,
    [Range(0, 7)] int WorkingDaysPerWeek,
    [Range(0, 24)] decimal DailyTargetHours,
    [Range(0, 1000)] decimal? HourlyRate);

public record UpdateContractRequest(
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    [Range(0, 168)] decimal WeeklyHours,
    [Range(0, 7)] int WorkingDaysPerWeek,
    [Range(0, 24)] decimal DailyTargetHours,
    [Range(0, 1000)] decimal? HourlyRate);

[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class ContractsController(ApplicationDbContext db) : ControllerBase
{
    private static readonly Func<Contract, ContractDto> ToDto =
        c => new ContractDto(c.Id, c.EmployeeId, c.ValidFrom, c.ValidTo, c.WeeklyHours, c.WorkingDaysPerWeek, c.DailyTargetHours, c.HourlyRate);

    [HttpGet("employees/{employeeId:guid}/contracts")]
    public async Task<ActionResult<IEnumerable<ContractDto>>> GetForEmployee(Guid employeeId)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId))
            return NotFound();

        var contracts = await db.Contracts
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.ValidFrom)
            .ToListAsync();
        return Ok(contracts.Select(ToDto));
    }

    [HttpGet("contracts/{id:guid}")]
    public async Task<ActionResult<ContractDto>> GetById(Guid id)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return contract is null ? NotFound() : Ok(ToDto(contract));
    }

    [HttpPost("employees/{employeeId:guid}/contracts")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<ActionResult<ContractDto>> Create(Guid employeeId, CreateContractRequest request)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId))
            return NotFound();

        if (await db.Contracts.AnyAsync(c => c.EmployeeId == employeeId && c.ValidFrom == request.ValidFrom))
            return Conflict($"Employee already has a contract valid from {request.ValidFrom}.");

        Contract contract;
        try
        {
            contract = Contract.Create(
                Guid.NewGuid(), employeeId, request.ValidFrom, request.ValidTo,
                request.WeeklyHours, request.WorkingDaysPerWeek, request.DailyTargetHours, request.HourlyRate);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = contract.Id }, ToDto(contract));
    }

    [HttpPut("contracts/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Update(Guid id, UpdateContractRequest request)
    {
        var contract = await db.Contracts.FindAsync(id);
        if (contract is null)
            return NotFound();

        if (await db.Contracts.AnyAsync(c => c.EmployeeId == contract.EmployeeId && c.ValidFrom == request.ValidFrom && c.Id != id))
            return Conflict($"Employee already has a contract valid from {request.ValidFrom}.");

        contract.ValidFrom = request.ValidFrom;
        contract.ValidTo = request.ValidTo;
        contract.WeeklyHours = request.WeeklyHours;
        contract.WorkingDaysPerWeek = request.WorkingDaysPerWeek;
        contract.DailyTargetHours = request.DailyTargetHours;
        contract.HourlyRate = request.HourlyRate;

        try
        {
            contract.Validate();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("contracts/{id:guid}")]
    [Authorize(Policy = "ManagerWrite")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var contract = await db.Contracts.FindAsync(id);
        if (contract is null)
            return NotFound();

        db.Contracts.Remove(contract);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
