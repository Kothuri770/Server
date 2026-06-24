using Microsoft.AspNetCore.Mvc;
using Server.Models;
using TrueCapture.Services;

namespace TrueCapture.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BatchLogController : ControllerBase
    {
        private readonly IBatchLogService _batchLogService;

        public BatchLogController(IBatchLogService batchLogService)
        {
            _batchLogService = batchLogService;
        }

        [HttpGet("{batchId}")]
        public async Task<ActionResult<List<BatchLogEntry>>> GetBatchLogs(int batchId)
        {
            try
            {
                var logs = await _batchLogService.GetBatchLogs(batchId);
                return Ok(logs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("summary/{batchId}")]
        public async Task<ActionResult<BatchLogSummary>> GetBatchLogSummary(int batchId)
        {
            try
            {
                var summary = await _batchLogService.GetBatchLogSummary(batchId);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<int>> LogBatchEntry([FromBody] BatchLogEntry logEntry)
        {
            try
            {
                logEntry.Timestamp = DateTime.Now;
                var result = await _batchLogService.LogBatchEntry(logEntry);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPut("{logId}/status")]
        public async Task<ActionResult<bool>> UpdateBatchLogStatus(int logId, [FromBody] UpdateBatchLogStatusRequest request)
        {
            try
            {
                var result = await _batchLogService.UpdateBatchLogStatus(logId, request.Status, request.Message);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tasks/{batchId}")]
        public async Task<ActionResult<List<BatchTaskLog>>> GetBatchTaskLogs(int batchId)
        {
            try
            {
                var logs = await _batchLogService.GetBatchTaskLogs(batchId);
                return Ok(logs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}