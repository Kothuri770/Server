using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Repositories;
using System.Text;
using TrueCapture.Services;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/monitor")]
    [Authorize]
    public class MonitorController : ControllerBase
    {
        private readonly IMonitorRepository _monitorRepo;
        private readonly ITestRepository _testRepo;
        private readonly IBatchLogService _batchLogService;
        private readonly ILogger<MonitorController> _logger;

        public MonitorController(IMonitorRepository monitorRepo, ITestRepository testRepo, IBatchLogService batchLogService, ILogger<MonitorController> logger)
        {
            _monitorRepo = monitorRepo;
            _testRepo = testRepo;
            _batchLogService = batchLogService;
            _logger = logger;
        }

        [HttpGet("jobs")]
        public async Task<IActionResult> GetJobs([FromQuery] string username, [FromQuery] string userType, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 100)
        {
            // Try to get role from claims first, fallback to query parameter
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                       ?? User.FindFirst("role")?.Value
                       ?? userType;
            _logger.LogDebug("GetJobs called with username: {Username}, role: {Role}, page: {Page}, size: {Size}", username, role, pageNumber, pageSize);
            var result = await _monitorRepo.GetJobsAsync(username, role, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("verify-batches")]
        public async Task<IActionResult> GetVerifyBatches([FromQuery] string username, [FromQuery] string userType, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 100)
        {
            // Try to get role from claims first, fallback to query parameter
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                       ?? User.FindFirst("role")?.Value
                       ?? userType;
            _logger.LogDebug("GetVerifyBatches called with username: {Username}, role: {Role}, page: {Page}, size: {Size}", username, role, pageNumber, pageSize);
            var result = await _monitorRepo.GetVerifyBatchesAsync(username, role, pageNumber, pageSize);

            return Ok(result);
        }

        [HttpGet("filter-columns")]
        public async Task<IActionResult> GetFilterColumns()
        {
            var columns = await _monitorRepo.GetFilterColumnsAsync();
            return Ok(columns);
        }

        [HttpPost("filter")]
        public async Task<IActionResult> ApplyFilter([FromBody] FilterInputDto filter, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 100)
        {
            var result = await _monitorRepo.GetFilteredDataAsync(filter, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpPost("batch-timings-bulk")]
        public async Task<IActionResult> GetBatchTimingsBulk([FromBody] IEnumerable<int> batchIds)
        {
            var timings = await _monitorRepo.GetBatchTaskTimingsBulkAsync(batchIds);
            return Ok(timings);
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var dashboard = await _monitorRepo.GetDashboardDataAsync();
            return Ok(dashboard);
        }

        [HttpPost("export")]
        public async Task<IActionResult> ExportJobs([FromBody] FilterInputDto filter)
        {
            var jobs = await _monitorRepo.GetFilteredDataAsync(filter);

            var csv = new StringBuilder();
            csv.AppendLine("ID,Batch Name,Type,Task,Status,Created On,Doc Count,Page Count,User");

            foreach (var job in jobs.Items)
            {
                csv.AppendLine($"{job.id},{job.batchname},{job.batchtype},{job.task},{job.batchstatus},{job.createdOn},{job.documentcount},{job.pagecount},{job.username}");
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"jobs_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        [HttpGet("test")]
        public async Task<IActionResult> TestDatabase()
        {
            try
            {
                var isConnected = await _testRepo.TestDatabaseConnectionAsync();
                var batchCount = await _testRepo.GetBatchCountAsync();
                var rawData = await _testRepo.GetRawBatchDataAsync();
                
                return Ok(new {
                    Connected = isConnected,
                    BatchCount = batchCount,
                    RawData = rawData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database test failed");
                return BadRequest(new { Error = "Database test failed" });
            }
        }

        [HttpGet("steps")]
        public async Task<IActionResult> GetSteps()
        {
            try
            {
                var steps = await _monitorRepo.GetStepsAsync();
                return Ok(steps);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("update-step/{batchId}")]
        public async Task<IActionResult> UpdateBatchStep(int batchId, [FromBody] UpdateStepRequest request)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the update batch step task
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "UPDATE_BATCH_STEP", $"Updating batch {batchId} to step {request.StepId}", null, username);
                
                var result = await _monitorRepo.UpdateBatchStepAsync(batchId, request.StepId, username);
                if (result)
                {
                    // Update task status to success
                    await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully updated batch {batchId} to step {request.StepId}");
                    
                    return Ok(new { message = "Batch step updated successfully" });
                }
                else
                {
                    // Update task status to failure
                    await _batchLogService.UpdateTaskStatusAsync(logId, "FAILED", "Failed to update batch step");
                    
                    return BadRequest(new { message = "Failed to update batch step" });
                }
            }
            catch (Exception ex)
            {
                // Log the failure
                var username = User.Identity?.Name ?? "system";
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "UPDATE_BATCH_STEP",
                    Status = "FAILED",
                    TaskDescription = "Update batch step",
                    ErrorMessage = ex.Message,
                    UserId = username
                });
                
                _logger.LogError(ex, "Error updating batch step for batch {BatchId}", batchId);
                return StatusCode(500, new { message = "Error updating batch step." });
            }
        }
    }
}