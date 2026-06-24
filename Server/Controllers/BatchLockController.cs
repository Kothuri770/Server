using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;
using Server.Services.Configuration;
using System.Threading.Tasks;

namespace Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BatchLockController : ControllerBase
{
    private readonly IBatchLockService _batchLockService;
    private readonly ILogger<BatchLockController> _logger;
    private readonly IConfigurationService _configService;

    public BatchLockController(IBatchLockService batchLockService, IConfigurationService configService,ILogger<BatchLockController> logger)
    {
        _batchLockService = batchLockService;
        _logger = logger;
        _configService = configService;
    }

    [HttpPost("acquire")]
    public async Task<ActionResult<AcquireLockResponse>> AcquireLock([FromBody] AcquireLockRequest request)
    {
        try
        {
            var userId = User.FindFirst("sub")?.Value ?? User.Identity?.Name ?? throw new UnauthorizedAccessException("User ID not found");
            var userName = User.Identity?.Name ?? userId;
            
            var response = await _batchLockService.AcquireLockAsync(request.BatchId, userId, userName, request.SessionId);
            
            if (response.Result == "LOCKED")
            {
                return StatusCode(423, response); // 423 Locked status code
            }
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock for batch {BatchId}", request.BatchId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("release")]
    public async Task<ActionResult<bool>> ReleaseLock([FromBody] ReleaseLockRequest request)
    {
        try
        {
            var userId = User.FindFirst("sub")?.Value ?? User.Identity?.Name ?? throw new UnauthorizedAccessException("User ID not found");
            
            var result = await _batchLockService.ReleaseLockAsync(request.BatchId, userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for batch {BatchId}", request.BatchId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<bool>> RefreshLock([FromBody] ReleaseLockRequest request)
    {
        try
        {
            var userId = User.FindFirst("sub")?.Value ?? User.Identity?.Name ?? throw new UnauthorizedAccessException("User ID not found");
            
            var result = await _batchLockService.RefreshLockAsync(request.BatchId, userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing lock for batch {BatchId}", request.BatchId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{batchId}/status")]
    public async Task<ActionResult<BatchLockInfo>> GetLockStatus(int batchId)
    {
        try
        {
            var lockInfo = await _batchLockService.GetLockStatusAsync(batchId);
            return Ok(lockInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lock status for batch {BatchId}", batchId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("active")]
    public async Task<ActionResult<List<BatchLockInfo>>> GetAllActiveLocks()
    {
        try
        {
            var locks = await _batchLockService.GetAllActiveLocksAsync();
            return Ok(locks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all active locks");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    
    [HttpGet("config/timeout")]
    public async Task<ActionResult<int>> GetLockTimeoutMinutes()
    {
        try
        {
            // Get the configured timeout from app settings
            var timeoutMinutes = await _configService.GetConfigurationsValue("BatchLockTimeoutMinutes");
            return Ok(Convert.ToInt32(timeoutMinutes?.ToString() ?? "20"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lock timeout configuration");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
