using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Contracts.System;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType<SystemHealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemHealthResponse> GetHealth()
    {
        return Ok(new SystemHealthResponse("ok", DateTimeOffset.UtcNow));
    }
}
