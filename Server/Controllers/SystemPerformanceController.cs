using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[Authorize(Roles = "admin,configeditor")]
[ApiController]
[Route("api/[controller]")]
public class SystemPerformanceController : ControllerBase
{
    private readonly ISystemPerformanceService _performanceService;

    public SystemPerformanceController(ISystemPerformanceService performanceService)
    {
        _performanceService = performanceService;
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<SystemPerformanceMetrics>> GetMetrics()
    {
        var metrics = await _performanceService.GetMetricsAsync();
        return Ok(metrics);
    }
}
