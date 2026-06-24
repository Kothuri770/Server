using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;
using Server.Services.Scanner;
using TrueCapture.Services;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/scanner")]
    [Authorize]
    public class ScannerController : ControllerBase
    {
        private readonly IScannerService _scannerService;
        private readonly IBatchLogService _batchLogService;
        private readonly ILogger<ScannerController> _logger;

        public ScannerController(IScannerService scannerService, IBatchLogService batchLogService, ILogger<ScannerController> logger)
        {
            _scannerService = scannerService;
            _batchLogService = batchLogService;
            _logger = logger;
        }

        [HttpGet("devices")]
        public async Task<IActionResult> GetScannerDevices()
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the get scanner devices task
                var logId = await _batchLogService.LogBatchTaskAsync(0, "GET_SCANNER_DEVICES", "Retrieving available scanner devices", null, username);
                
                var devices = await _scannerService.GetScannerDevicesAsync();
                
                // Update task status to success
                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully retrieved {devices.Count} scanner devices");
                
                return Ok(devices);
            }
            catch (Exception ex)
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = 0,
                    TaskType = "GET_SCANNER_DEVICES",
                    Status = "FAILED",
                    TaskDescription = "Get scanner devices",
                    ErrorMessage = ex.Message,
                    UserId = username
                });
                
                _logger.LogError(ex, "Error getting scanner devices");
                return StatusCode(500, "Scanner service unavailable");
            }
        }

        [HttpPost("scan")]
        public async Task<IActionResult> Scan(long batchId, int docId, [FromQuery] string deviceId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the scan task
                var logId = await _batchLogService.LogBatchTaskAsync((int)batchId, "SCAN", $"Scanning from device '{deviceId}' for batch {batchId}", docId, username);
                
                var results = await _scannerService.ScanFromDeviceAsync(batchId, docId, deviceId);
                
                // Update task status to success
                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully scanned {results.Count} images from device '{deviceId}' for batch {batchId}");
                
                return Ok(results);
            }
            catch (Exception ex)
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = (int)batchId,
                    TaskType = "SCAN",
                    Status = "FAILED",
                    TaskDescription = $"Scan from device '{deviceId}'",
                    ErrorMessage = ex.Message,
                    DocumentId = docId,
                    UserId = username
                });
                
                _logger.LogError(ex, "Error scanning from device");
                return StatusCode(500, ex.Message);
            }
        }
        
        [HttpPost("update-step-status")]
        public async Task<IActionResult> UpdateStepStatus([FromBody] UpdateStepStatusRequest request)
        {
            try
            {
                var success = await _scannerService.UpdateStepStatusForSeparationModeAsync(request.SeparationMode);
                
                if (success)
                {
                    return Ok(new { message = "Step status updated successfully" });
                }
                else
                {
                    return StatusCode(500, new { message = "Failed to update step status" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating step status for separation mode: {SeparationMode}", request.SeparationMode);
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}