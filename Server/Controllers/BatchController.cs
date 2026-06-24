using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using Server.Services.Scanner;
using System.Data;
using TrueCapture.Services;
using Server.Models;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/batch")]
    [Authorize]
    public class BatchController : ControllerBase
    {
        private readonly IBatchRepository _batchRepo;
        private readonly IFileStorageService _fileService;
        private readonly ILogger<BatchController> _logger;
        private readonly IConfigurationService _configService;
        private readonly IBatchLogService _batchLogService;
        private readonly IPurgeConfigService _purgeConfigService;

        public BatchController(
            IBatchRepository batchRepo,
            IFileStorageService fileService,
             IConfigurationService configService,
            IBatchLogService batchLogService,
            IPurgeConfigService purgeConfigService,
            ILogger<BatchController> logger)
        {
            _batchRepo = batchRepo;
            _fileService = fileService;
            _configService = configService;
            _batchLogService = batchLogService;
            _purgeConfigService = purgeConfigService;
            _logger = logger;
        }

        [HttpGet("next-number")]
        public async Task<ActionResult<long>> GetNextBatchNumber()
        {
            return Ok(await _batchRepo.GetNextBatchNumberAsync());
        }
        [HttpGet("batch-prefix")]
        public async Task<ActionResult<string>> GetBatchprefixAsync()
        {
            return Ok(await _configService.GetConfigurationsValue("Batch Prefix"));
        }

        [HttpGet("applications")]
        public async Task<ActionResult<List<ApplicationDto>>> GetApplications()
        {
            var apps = await _batchRepo.GetApplicationNamesAsync();
            return Ok(apps.Select(a => new ApplicationDto { Id = int.Parse(a.id), Name = a.name, SeparationMode = a.SeparationMode }));
        }

        [HttpGet("document-tree")]
        public async Task<ActionResult<List<DocTypeDto>>> GetDocumentTree([FromQuery] int appId, [FromQuery] long batchId)
        {
            if (appId == 0) return Ok(new List<DocTypeDto>());

            var docs = await _batchRepo.GetDocumentTypesAsync(new GetDocumentRequest { AppId = appId });
            var result = docs.Select(d => new DocTypeDto
            {
                Id = d.Id,
                Name = d.Name,
                NDocId = d.NDocId
            }).ToList();

            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<int>> CreateBatch([FromBody] CreateBatchRequest request)
        {

            try
            {
                var username = User.Identity?.Name ?? "system";
                
                // Start timing for CREATE_BATCH task
                
                
                var batchId = await _batchRepo.InsertBatchAsync(
                    request.BatchName, DateTime.Now, request.BatchTypeId, "A", 1, username);
                
                // Fetch the actual batch record to get the potentially auto-generated name
                var batchRecord = await _batchRepo.GetBatchByIdAsync(batchId);
                var actualBatchName = batchRecord?.BatchName ?? request.BatchName;

                // Start timing for CREATE_BATCH task
                await _batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 1, Status = "Start" });
                await _batchRepo.UpdateBatchAsync(batchId, request.BatchTypeId);
                
                // Log the create batch task after batch is created
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "CREATE_BATCH", $"Creating batch '{actualBatchName}' with type ID {request.BatchTypeId}", null, username);
                
                // Update task status to success
                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully created batch {batchId}");
                

                
                return CreatedAtAction(nameof(GetBatch), new { batchId }, new { batchId, BatchName = actualBatchName });
            }
            catch (Exception ex)
            {

                
                _logger.LogError(ex, "Error creating batch {BatchName}", request.BatchName);
                
                // Only log failure if we have a batch ID (batch was partially created)
                // If batch creation failed completely, we can't log with a specific batch ID
                // The error will be logged in application logs instead
                return StatusCode(500, new { message = "An error occurred while creating the batch." });
            }
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitBatch([FromBody] BatchSubmitModel model)
        {

            try
            {
                var username = User.Identity?.Name ?? "system";
                
                // Check separation mode from configuration
                string separationMode = "Manual";
                var batch = await _batchRepo.GetBatchByIdAsync((int)model.BatchId);
                if (batch != null)
                {
                    var appType = await _configService.GetObjectTypeByIdAsync(batch.BatchTypeId);
                    if (appType != null && !string.IsNullOrEmpty(appType.SeparationMode) && appType.SeparationMode != "Global")
                    {
                        separationMode = appType.SeparationMode;
                    }
                    else
                    {
                        var globalMode = await _configService.GetConfigurationsValue("Separation Mode");
                        if (!string.IsNullOrEmpty(globalMode))
                        {
                            separationMode = globalMode;
                        }
                    }
                }

                // If separation mode is not auto, then only check for uncategorized images
                if (separationMode.Trim().ToLower() != "auto")
                {
                    var hasUncategorized = await _batchRepo.HasUncategorizedImagesAsync((int)model.BatchId);
                    if (hasUncategorized)
                    {
                        // Log the categorization failure
                        await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                        {
                            BatchId = (int)model.BatchId,
                            TaskType = "SUBMIT_BATCH",
                            Status = "FAILED",
                            TaskDescription = "Submit batch - uncategorized images",
                            ErrorMessage = "All images must be categorized before submission",
                            UserId = username
                        });
                        
                        return BadRequest("All images must be categorized before submission");
                    }
                }

                // Log the submit batch task
                var logId = await _batchLogService.LogBatchTaskAsync((int)model.BatchId, "SUBMIT_BATCH", $"Submitting batch {model.BatchId} for processing", null, username);
                
                await _batchRepo.MoveToNextStepAsync((int)model.BatchId, username);

                // Update task status to success
                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully submitted batch {model.BatchId} for processing");

                // Log timing for SUBMIT_BATCH task with success status
                await _batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = (int)model.BatchId, TaskId = 1, Status = "End" });
                return Ok(new { message = "Batch submitted successfully", nextStep = "Verification" });
            }
            catch (Exception ex)
            {

                
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = (int)model.BatchId,
                    TaskType = "SUBMIT_BATCH",
                    Status = "FAILED",
                    TaskDescription = "Submit batch",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });
                
                _logger.LogError(ex, "Error submitting batch {BatchId}", model.BatchId);
                return StatusCode(500, new { message = "An error occurred while submitting the batch." });
            }
        }

        [HttpPost("save-details")]
        public async Task<IActionResult> SaveBatchDetails([FromBody] SaveBatchModel model)
        {

            try
            {
                // Start and immediately end timing for SAVE_BATCH_DETAILS task
                // Log the save batch details task
                var logId = await _batchLogService.LogBatchTaskAsync(model.BatchId, "SAVE_BATCH_DETAILS", $"Saving batch details for batch {model.BatchId}", null, model.Username);
                
                // Update batch details in database using repository
                bool success = await _batchRepo.SaveBatchDetailsAsync(model);
                
                if (success)
                {
                    // Update task status to success
                    await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully saved batch details for batch {model.BatchId}");
                    return Ok(new { message = "Batch details saved successfully" });
                }
                else
                {
                    // Update task status to failure
                    await _batchLogService.UpdateTaskStatusAsync(logId, "FAILED", "Failed to save batch details"); 
                    return BadRequest("Failed to save batch details");
                }
            }
            catch (Exception ex)
            {

                
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = model.BatchId,
                    TaskType = "SAVE_BATCH_DETAILS",
                    Status = "FAILED",
                    TaskDescription = "Save batch details",
                    ErrorMessage = ex.Message,
                    UserId = model.Username
                });
                
                _logger.LogError(ex, "Error saving batch details {BatchId}", model.BatchId);
                return StatusCode(500, new { message = "An error occurred while saving batch details." });
            }
        }

        [HttpGet("{batchId}")]
        public async Task<ActionResult<BatchDto>> GetBatch(int batchId)
        {
            try
            {
                // #19: Removed DB logging from read-only GET — use ILogger only
                var batch = await _batchRepo.GetBatchByIdAsync(batchId);
                
                if (batch != null)
                {
                    return Ok(batch);
                }
                else
                {
                    return NotFound();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch {BatchId}", batchId);
                return StatusCode(500, new { message = "An error occurred while retrieving batch details." });
            }
        }

        [HttpGet("{batchId}/images")]
        public async Task<ActionResult<IEnumerable<BatchImageWithDocNameDto>>> GetBatchImages(int batchId)
        {
            try
            {
                // #19: Removed DB logging from read-only GET — use ILogger only
                var images = await _batchRepo.GetBatchImagesAsync(batchId);
                return Ok(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch images {BatchId}", batchId);
                return StatusCode(500, new { message = "An error occurred while retrieving batch images." });
            }
        }

        // ✔️ FIXED: Proper file serving with correct path resolution
        [HttpGet("{batchId}/file/{fileName}")]
        public async Task<IActionResult> GetFile(int batchId, string fileName)
        {
            try
            {
                // #19: Removed DB logging from read-only GET — use ILogger only
                var decodedFileName = Uri.UnescapeDataString(fileName);
                var bytes = await _fileService.GetFileAsync(decodedFileName, batchId);

                if (bytes == null || bytes.Length == 0)
                {
                    _logger.LogWarning("File not found: Batch {BatchId}, File {FileName}", batchId, decodedFileName);
                    return NotFound(new { message = "File not found." });
                }

                var mimeType = GetMimeType(decodedFileName);
                return File(bytes, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file {FileName} for batch {BatchId}", fileName, batchId);
                return StatusCode(500, new { message = "An error occurred while retrieving the file." });
            }
        }

        private static string GetMimeType(string fileName) => Path.GetExtension(fileName).ToLower() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            ".tif" or ".tiff" => "image/tiff",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        
        [HttpGet("{batchId}/timings")]
        public async Task<ActionResult<List<BatchTaskTimeDto>>> GetBatchTaskTimings(int batchId)
        {
            try
            {
                var timings = await _batchLogService.GetBatchTaskTimingsAsync(batchId);
                return Ok(timings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting task timings for batch {BatchId}", batchId);
                return StatusCode(500, new { message = "Error retrieving task timings" });
            }
        }
        
        [HttpPost("move-step")]
        public async Task<IActionResult> MoveBatchToStep([FromBody] MoveStepRequest request)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the move batch to step task
                var logId = await _batchLogService.LogBatchTaskAsync((int)request.BatchId, "MOVE_BATCH_STEP", 
                    $"Moving batch {request.BatchId} to step {request.StepId}", null, username);
                
                // Move the batch to the specified step
                await _batchRepo.MoveToSpecificStepAsync((int)request.BatchId, request.StepId, username);
                
                // Update task status to success
                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, 
                    $"Successfully moved batch {request.BatchId} to step {request.StepId}");
                
                return Ok(new { message = "Batch moved to step successfully" });
            }
            catch (Exception ex)
            {
                var username = User.Identity?.Name ?? "system";
                
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = (int)request.BatchId,
                    TaskType = "MOVE_BATCH_STEP",
                    Status = "FAILED",
                    TaskDescription = $"Move batch to step {request.StepId}",
                    ErrorMessage = ex.Message,
                    UserId = username
                });
                
                _logger.LogError(ex, "Error moving batch {BatchId} to step {StepId}", request.BatchId, request.StepId);
                return StatusCode(500, new { message = "An error occurred while moving the batch to the specified step." });
            }
        }

        [HttpGet("latest")]
        public async Task<ActionResult<List<Batch>>> GetLatestBatches([FromQuery] int count = 100)
        {
            try
            {
                var batches = await _batchRepo.GetLatestBatchesAsync(count);
                return Ok(batches);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest batches");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet("{batchId}/logfiles")]
        public async Task<ActionResult<List<LogFile>>> GetBatchLogFiles(int batchId)
        {
            try
            {
                var logFiles = await _batchLogService.GetBatchLogFilesAsync(batchId);
                return Ok(logFiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving log files for batch {BatchId}", batchId);
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet("{batchId}/logfile/{fileName}")]
        public async Task<IActionResult> GetLogFileContent(int batchId, string fileName)
        {
            try
            {
                var content = await _batchLogService.GetLogFileContentAsync(batchId, fileName);
                if (content == null)
                {
                    return NotFound("Log file not found or invalid path");
                }

                return Content(content, "text/plain");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading log file {FileName} for batch {BatchId}", fileName, batchId);
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet("purge-config")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<PurgeConfigDto>> GetPurgeConfig()
        {
            return Ok(await _purgeConfigService.GetConfigAsync());
        }

        [HttpPost("purge-config")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<PurgeResponseDto>> SavePurgeConfig([FromBody] PurgeConfigDto request)
        {
            var response = new PurgeResponseDto { Success = true };
            
            // Validate the date range
            if (request.EndDate > DateTime.UtcNow.AddDays(-30))
            {
                response.Success = false;
                response.Message = "Error: Cannot delete data from the last 30 days. Please select an end date at least 30 days in the past.";
                return BadRequest(response);
            }
            if (request.StartDate > request.EndDate)
            {
                response.Success = false;
                response.Message = "Error: Start Date cannot be later than End Date.";
                return BadRequest(response);
            }
            
            try
            {
                await _purgeConfigService.SaveConfigAsync(request);
                response.Message = "Configuration saved successfully. The background service will run according to schedule.";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"Critical error during configuration save: {ex.Message}";
                _logger.LogError(ex, "Critical error during batch purge configuration save");
                return StatusCode(500, response);
            }
        }


    }
}
