using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Api.Controllers;

// issue #15: gesetzliche Feiertage — computed (GermanPublicHolidays), not stored, so there's
// nothing to seed and no controller writes; just a range query over the calculation.
[ApiController]
[Route("api/public-holidays")]
[Authorize(Policy = "ApiRead")]
public class PublicHolidaysController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<PublicHoliday>> GetInRange(DateOnly start, DateOnly end)
    {
        if (end < start)
            return BadRequest("'end' must not be before 'start'.");

        return Ok(GermanPublicHolidays.InRange(start, end));
    }
}
