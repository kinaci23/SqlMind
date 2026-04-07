using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlMind.Core;

namespace SqlMind.API.Controllers;

[ApiController]
[Route("api/v1/logs")]
[AllowAnonymous]
public class LogsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] int limit = 100)
        => Ok(AppLogger.GetAll().TakeLast(limit));

    [HttpDelete]
    public IActionResult Clear() { AppLogger.Clear(); return Ok(); }
}
