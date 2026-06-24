using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using System.Text.Json;
using TrueCapture.Services;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/verify")]
    [Authorize]
    public class VerifyController : ControllerBase
    {
        private readonly IVerifyRepository _verifyRepo;
        private readonly IBatchRepository _mainRepo;
        private readonly IOcrConnectorService _ocrConnectorService;
        private readonly IConfigurationService _configService;
        private readonly IBatchLogService _batchLogService;
        private readonly ISeparationService _separationService;
        private readonly IOcrEngineService _ocrEngine;
        private readonly ILogger<VerifyController> _logger;

        public VerifyController(IVerifyRepository verifyRepo, IBatchRepository mainRepo, IOcrConnectorService ocrConnectorService, IConfigurationService configService, IBatchLogService batchLogService, ISeparationService separationService, IOcrEngineService ocrEngine, ILogger<VerifyController> logger)
        {
            _verifyRepo = verifyRepo;
            _mainRepo = mainRepo;
            _ocrConnectorService = ocrConnectorService;
            _logger = logger;
            _configService = configService;
            _batchLogService = batchLogService;
            _separationService = separationService;
            _ocrEngine = ocrEngine;
        }

        [HttpGet("documents/{batchId}")]
        public async Task<IActionResult> GetDocuments(int batchId)
        {
            var docs = await _verifyRepo.GetDocumentsForVerifyAsync(batchId);
            return Ok(docs);
        }

        [HttpGet("pages/{batchId}")]
        public async Task<IActionResult> GetPages(int batchId)
        {
            var pages = await _verifyRepo.GetPagesForVerifyAsync(batchId);
            return Ok(pages);
        }

        [HttpGet("index-fields/{docTypeId}")]
        public async Task<IActionResult> GetIndexFields(int docTypeId)
        {
            var fields = await _verifyRepo.GetIndexFieldsAsync(docTypeId);
            return Ok(fields);
        }

        [HttpGet("batch-index-fields/{batchTypeId}")]
        public async Task<IActionResult> GetBatchIndexFieldsAsync(int batchTypeId)
        {
            var fields = await _verifyRepo.GetBatchIndexFieldsAsync(batchTypeId);
            return Ok(fields);
        }

        [HttpGet("index-values/{batchId}/{docId}")]
        public async Task<IActionResult> GetIndexValues(int batchId, int docId)
        {
            var values = await _verifyRepo.GetIndexValuesForVerifyAsync(batchId, docId);
            return Ok(values);
        }

        [HttpGet("batch-index-values/{batchId}")]
        public async Task<IActionResult> GetBatchIndexValues(int batchId)
        {
            var values = await _verifyRepo.GetBatchIndexValuesForVerifyAsync(batchId);
            return Ok(values);
        }

        [HttpGet("batch-type-name/{batchId}")]
        public async Task<IActionResult> GetBatchTypeName(int batchId)
        {
            var name = await _verifyRepo.GetBatchTypeNameAsync(batchId);
            return Ok(name);
        }

        [HttpGet("document-type-names/{batchId}")]
        public async Task<IActionResult> GetDocumentTypeNames(int batchId)
        {
            var names = await _verifyRepo.GetDocumentTypeNamesAsync(batchId);
            return Ok(names);
        }

        [HttpGet("document-names/{batchId}/{docTypeId}")]
        public async Task<IActionResult> GetDocumentNames(int batchId, int docTypeId)
        {
            var names = await _verifyRepo.GetDocumentNamesAsync(batchId, docTypeId);
            return Ok(names);
        }

        [HttpGet("document-pages/{batchId}/{docIdHandle}")]
        public async Task<IActionResult> GetDocumentPages(int batchId, int docIdHandle)
        {
            var pages = await _verifyRepo.GetDocumentPageNumbersAsync(batchId, docIdHandle);
            return Ok(pages);
        }

        [HttpGet("batch-info/{batchId}")]
        public async Task<IActionResult> GetBatchInfo(int batchId)
        {
            // Get batch info from main repo
            var batch = await _mainRepo.GetBatchByIdAsync(batchId);
            await _batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 5, Status = "Start" });
            return Ok(batch);
        }

        [HttpPost("save-index")]
        public async Task<IActionResult> SaveIndexData([FromBody] SaveIndexDto? data)
        {
            if (data == null) return BadRequest("No data provided");
            if (data.Fields == null) return BadRequest("Field data is missing");

            try
            {
                // Separate batch properties from document properties
                var batchProperties = data.Fields.Where(f => f.Key != null && f.Key.StartsWith("batch_")).ToDictionary(f => f.Key, f => f.Value);
                var documentProperties = data.Fields.Where(f => f.Key != null && !f.Key.StartsWith("batch_")).ToDictionary(f => f.Key, f => f.Value);

                // Save document properties to dynamic document table
                if (documentProperties.Any())
                {
                    var success = await _verifyRepo.SaveDocumentIndexDataAsync(data.DocId, documentProperties);
                    if (!success) return BadRequest("Failed to save document index data");
                }

                // Save OCR results for document properties


                // Remove "batch_" prefix from batch properties before saving to database
                var normalizedBatchProperties = batchProperties.ToDictionary(
                    kvp => kvp.Key.Substring("batch_".Length), // Remove "batch_" prefix
                    kvp => kvp.Value
                );

                // Save batch properties
                if (normalizedBatchProperties.Any())
                {
                    var success = await _verifyRepo.SaveBatchIndexDataAsync(data.BatchId, normalizedBatchProperties);
                    if (!success) return BadRequest("Failed to save batch index data");
                }





                return Ok(new { message = "Document verified successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving index data for batch {BatchId}", data.BatchId);
                return BadRequest(new { message = "Error saving index data. Please try again." });
            }
        }

        [HttpGet("page-image/{pageId}")]
        public async Task<IActionResult> GetPageImage(int pageId)
        {
            var imageData = await _verifyRepo.GetPageImageAsync(pageId);
            if (string.IsNullOrEmpty(imageData)) return NotFound("Image not found");
            return Ok(imageData);
        }

        [HttpGet("page-image-file/{pageId}")]
        public async Task<IActionResult> GetPageImageFile(int pageId)
        {
            try
            {
                // #14: Stream file directly instead of loading into memory
                var (filePath, fileName) = await _verifyRepo.GetPageFilePathAsync(pageId);

                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                {
                    return NotFound("Image file not found");
                }

                var contentType = GetContentType(fileName);
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
                return File(stream, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving image file for page {PageId}", pageId);
                return StatusCode(500, new { message = "Error retrieving image file." });
            }
        }

        [HttpGet("page-image-base64/{pageId}")]
        public IActionResult GetPageImageAsBase64(int pageId)
        {
            return BadRequest("This endpoint is deprecated to prevent server memory exhaustion. Please use the streaming endpoint /api/verify/page-image-file/{pageId} instead.");
        }

        private string GetContentType(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        [HttpPost("move-to-next-step/{batchId}")]
        public async Task<IActionResult> MoveToNextStep(int batchId)
        {

            try
            {
                var username = User.Identity?.Name ?? "system";
                // Log the move to next step task
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "MOVE_TO_NEXT_STEP", $"Moving batch {batchId} to next step", null, username);

                // Hierarchical Connector Resolution to update the batch OcrType
                // 1. Check Application (Batch Type) Level
                var batch = await _mainRepo.GetBatchByIdAsync(batchId);
                var activeConnector = batch != null ? await _ocrConnectorService.GetOcrConnectorByApplicationIdAsync(batch.BatchTypeId) : null;
                
                // 2. Fallback to Global Default
                if (activeConnector == null)
                {
                    activeConnector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
                }

                if (activeConnector?.Provider?.Name != null)
                {
                    // Update the batch table with the OCR type from the resolved connector
                    await _mainRepo.UpdateBatchOcrTypeAsync(batchId, activeConnector.Provider.Name);
                }

                await _mainRepo.MoveToNextStepAsync(batchId, username);

                // Update task status to success
                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully moved batch {batchId} to next step");


                await _batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 5, Status = "End" });
                return Ok(new { message = "Batch moved to next step successfully" });
            }
            catch (Exception ex)
            {
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "MOVE_TO_NEXT_STEP",
                    Status = "FAILED",
                    TaskDescription = "Move to next step",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });

                return StatusCode(500, new { message = "Error moving batch to next step: " + ex.Message });
            }
        }

        [HttpPost("hold-batch/{batchId}")]
        public async Task<IActionResult> HoldBatch(int batchId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";

                // Log the hold batch task
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "HOLD_BATCH", $"Holding batch {batchId}", null, username);

                var success = await _mainRepo.HoldBatchAsync(batchId, username);
                if (success)
                {
                    // Update task status to success
                    await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully held batch {batchId}");

                    return Ok(new { message = "Batch held successfully" });
                }
                else
                {
                    // Update task status to failure
                    await _batchLogService.UpdateTaskStatusAsync(logId, "FAILED", "Failed to hold batch");

                    return BadRequest(new { message = "Failed to hold batch" });
                }
            }
            catch (Exception ex)
            {
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "HOLD_BATCH",
                    Status = "FAILED",
                    TaskDescription = "Hold batch",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });

                return StatusCode(500, new { message = $"Error holding batch: {ex.Message}" });
            }
        }

        [HttpPost("update-batch-status/{batchId}")]
        public async Task<IActionResult> UpdateBatchStatus(int batchId, [FromBody] UpdateBatchStatusRequest request)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";

                // Log the update batch status task
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "UPDATE_BATCH_STATUS", $"Updating batch {batchId} status to {request.Status}", null, username);

                var success = await _mainRepo.UpdateBatchStatusAsync(batchId, request.Status, username);
                if (success)
                {
                    // Update task status to success
                    await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully updated batch {batchId} status to {request.Status}");

                    return Ok(new { message = "Batch status updated successfully" });
                }
                else
                {
                    // Update task status to failure
                    await _batchLogService.UpdateTaskStatusAsync(logId, "FAILED", "Failed to update batch status");

                    return BadRequest(new { message = "Failed to update batch status" });
                }
            }
            catch (Exception ex)
            {
                // Log the failure
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "UPDATE_BATCH_STATUS",
                    Status = "FAILED",
                    TaskDescription = "Update batch status",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });

                return StatusCode(500, new { message = $"Error updating batch status: {ex.Message}" });
            }
        }


        [HttpPost("run-ocr/{batchId}")]
        public async Task<IActionResult> RunOcr(int batchId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "OCR_PROCESSING", $"Running OCR for batch {batchId}", null, username);

                var documents = await _verifyRepo.GetDocumentsForVerifyAsync(batchId);
                var pages = await _verifyRepo.GetPagesForVerifyAsync(batchId);

                var batch = await _mainRepo.GetBatchByIdAsync(batchId);
                
                // Hierarchical Connector Resolution (2-tier: Application -> Global Default)
                // 1. Check Application (Batch Type) Level
                var activeConnector = batch != null ? await _ocrConnectorService.GetOcrConnectorByApplicationIdAsync(batch.BatchTypeId) : null;
                
                // 2. Fallback to Global Default
                if (activeConnector == null)
                {
                    activeConnector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
                }

                var providerName = activeConnector?.Provider?.Name ?? "tesseract";
                var configData = activeConnector?.ConfigData;

                foreach (var document in documents)
                {
                    var documentPages = pages.Where(p => p.DocId == document.DocId).OrderBy(p => p.DocPage).ToList();
                    if (!documentPages.Any()) continue;

                    await _ocrEngine.ProcessDocumentAsync(providerName, document, documentPages, batchId, configData);
                }

                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully completed OCR for batch {batchId}");
                return Ok(new { message = "OCR processing completed successfully" });
            }
            catch (Exception ex)
            {
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "OCR_PROCESSING",
                    Status = "FAILED",
                    TaskDescription = "Run OCR",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name
                });
                return StatusCode(500, new { message = $"Error running OCR: {ex.Message}" });
            }
        }

        [HttpGet("get-ocr-results/{docId}")]
        public async Task<IActionResult> GetOcrResults(int docId)
        {
            try
            {
                var ocrResults = await _verifyRepo.GetOcrResultsByDocumentIdAsync(docId);
                return Ok(ocrResults);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error getting OCR results: {ex.Message}" });
            }
        }

        [HttpGet("zones/{docTypeId}")]
        public async Task<IActionResult> GetZonesForDocType(int docTypeId)
        {
            try
            {
                var zones = await _verifyRepo.GetZonesForDocumentTypeAsync(docTypeId);
                return Ok(zones);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error getting zones for document type: {ex.Message}" });
            }
        }

        [HttpPost("extract-ocr-value/{docId}/{zoneId}")]
        public async Task<IActionResult> ExtractOcrValue(int docId, int zoneId)
        {
            try
            {
                var result = await _verifyRepo.ExtractOcrValueAsync(docId, zoneId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error extracting OCR value: {ex.Message}" });
            }
        }

        [HttpPost("run-default-ocr/{batchId}")]
        public async Task<IActionResult> RunDefaultOcr(int batchId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "RUN_DEFAULT_OCR", $"Running default OCR for batch {batchId}", null, username);

                var batch = await _mainRepo.GetBatchByIdAsync(batchId);

                // Hierarchical Connector Resolution (2-tier: Application -> Global Default)
                // 1. Check Application (Batch Type) Level
                var activeConnector = batch != null ? await _ocrConnectorService.GetOcrConnectorByApplicationIdAsync(batch.BatchTypeId) : null;
                
                // 2. Fallback to Global Default
                if (activeConnector == null)
                {
                    activeConnector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
                }

                if (activeConnector == null)
                {
                    await _batchLogService.UpdateTaskStatusAsync(logId, "FAILED", "No OCR connector configured for this application or as global default");
                    return BadRequest(new { message = "No OCR connector configured for this application or as global default" });
                }

                var providerName = activeConnector.Provider?.Name ?? "tesseract";
                var configData = activeConnector.ConfigData;
                var documents = await _verifyRepo.GetDocumentsForVerifyAsync(batchId);
                var pages = await _verifyRepo.GetPagesForVerifyAsync(batchId);

                foreach (var document in documents)
                {
                    var documentPages = pages.Where(p => p.DocId == document.DocId).OrderBy(p => p.DocPage).ToList();
                    if (!documentPages.Any()) continue;

                    await _ocrEngine.ProcessDocumentAsync(providerName, document, documentPages, batchId, configData);
                }

                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully completed OCR for batch {batchId} with {providerName} (Application-level: {batch?.BatchTypeId})");
                return Ok(new { message = $"OCR processing completed with {providerName}" });
            }
            catch (Exception ex)
            {
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "RUN_DEFAULT_OCR",
                    Status = "FAILED",
                    TaskDescription = "Run default OCR",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });
                return StatusCode(500, new { message = $"Error running default OCR: {ex.Message}" });
            }
        }

        [HttpPost("run-azure-doc-intel/{batchId}")]
        public async Task<IActionResult> RunAzureDocIntel(int batchId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "RUN_AZURE_DOC_INTEL", $"Running Azure Document Intelligence for batch {batchId}", null, username);

                var documents = await _verifyRepo.GetDocumentsForVerifyAsync(batchId);
                var allPages = (await _verifyRepo.GetPagesForVerifyAsync(batchId)).ToList();

                foreach (var document in documents)
                {
                    var documentPages = allPages
                        .Where(p => p.DocId == document.DocId)
                        .OrderBy(p => p.DocPage)
                        .ToList();

                    if (documentPages.Any())
                    {
                        await _ocrEngine.ProcessDocumentAsync("azuredocintel", document, documentPages, batchId);
                    }
                }

                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully completed Azure Document Intelligence for batch {batchId}");
                return Ok(new { message = "Azure Document Intelligence processing completed successfully" });
            }
            catch (Exception ex)
            {
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "RUN_AZURE_DOC_INTEL",
                    Status = "FAILED",
                    TaskDescription = "Run Azure Document Intelligence",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });
                return StatusCode(500, new { message = $"Error running Azure Document Intelligence: {ex.Message}" });
            }
        }

        [HttpGet("get-azure-doc-intel-results/{docId}")]
        public async Task<IActionResult> GetAzureDocIntelResults(int docId)
        {
            try
            {
                var result = await _verifyRepo.GetAzureDocIntelResultByDocumentIdAsync(docId);
                if (result == null)
                {
                    // Return a dummy object with empty/null properties instead of NotFound to prevent client-side console 404 errors
                    return Ok(new AzureDocIntelResult { DocId = docId, AnalysisResult = string.Empty });
                }
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error getting Azure Document Intelligence results: " + ex.Message });
            }
        }

        [HttpPost("save-azure-doc-intel-result/{docId}")]
        public async Task<IActionResult> SaveAzureDocIntelResult(int docId, [FromBody] JsonElement requestData)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";

                // Safely retrieve the analysisResult property (handle both camelCase and PascalCase)
                string? analysisResult = null;
                if (requestData.TryGetProperty("analysisResult", out var resultProp))
                {
                    analysisResult = resultProp.GetString();
                }
                else if (requestData.TryGetProperty("AnalysisResult", out var resultPropPascal))
                {
                    analysisResult = resultPropPascal.GetString();
                }

                if (string.IsNullOrEmpty(analysisResult))
                {
                    Console.WriteLine($"[VerifyController] Error: analysisResult is null or empty for docId {docId}");
                    return BadRequest(new { message = "Analysis result is required" });
                }

                // Get the batch ID for logging purposes
                var documentInfo = await _verifyRepo.GetDocumentInfoByIdAsync(docId);
                if (documentInfo != null)
                {
                    Console.WriteLine($"[VerifyController] Saving Azure AI result for DocId: {docId}, BatchId: {documentInfo.BatchId}");
                    
                    // Log the save Azure Doc Intel result task
                    var logId = await _batchLogService.LogBatchTaskAsync(documentInfo.BatchId, "SAVE_AZURE_DOC_INTEL_RESULT", $"Saving Azure Document Intelligence result for document {docId}", null, username);

                    // Save the updated Azure Document Intelligence result to the database
                    await _verifyRepo.SaveAzureDocIntelResultAsync(docId, analysisResult);

                    // Update task status to success
                    await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully saved Azure Document Intelligence result for document {docId}");
                }
                else
                {
                    Console.WriteLine($"[VerifyController] Saving Azure AI result for DocId: {docId} (No document info found)");
                    // Save the updated Azure Document Intelligence result to the database
                    await _verifyRepo.SaveAzureDocIntelResultAsync(docId, analysisResult);
                }

                return Ok(new { message = "Azure Document Intelligence result saved successfully" });
            }
            catch (Exception ex)
            {
                // Log the failure if we can get the batch ID
                try
                {
                    var documentInfo = await _verifyRepo.GetDocumentInfoByIdAsync(docId);
                    if (documentInfo != null)
                    {
                        await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                        {
                            BatchId = documentInfo.BatchId,
                            TaskType = "SAVE_AZURE_DOC_INTEL_RESULT",
                            Status = "FAILED",
                            TaskDescription = "Save Azure Document Intelligence result",
                            ErrorMessage = ex.Message,
                            UserId = User.Identity?.Name ?? "system"
                        });
                    }
                }
                catch { /* Ignore logging errors */ }

                return StatusCode(500, new { message = "Error saving Azure Document Intelligence result: " + ex.Message });
            }
        }

        [HttpGet("{batchId}/timings")]
        public async Task<ActionResult<List<BatchTaskTimeDto>>> GetBatchTaskTimings(int batchId)
        {
            try
            {
                var timings = await _batchLogService.GetBatchTaskTimingsAsync(batchId);
                return Ok(timings);
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Error retrieving task timings" });
            }
        }
        [HttpPost("run-separation/{batchId}")]
        public async Task<IActionResult> RunSeparation(int batchId)
        {
            try
            {
                var username = User.Identity?.Name ?? "system";
                var logId = await _batchLogService.LogBatchTaskAsync(batchId, "AUTO_SEPARATION", $"Running manual autoseparation for batch {batchId}", null, username);

                var batch = await _mainRepo.GetBatchByIdAsync(batchId);
                if (batch == null) return NotFound("Batch not found");

                await _separationService.ProcessBatchSeparationAsync(batchId, batch.BatchTypeId, _verifyRepo, _configService, _batchLogService);

                await _batchLogService.UpdateTaskStatusAsync(logId, "SUCCESS", null, $"Successfully completed manual autoseparation for batch {batchId}");
                return Ok(new { message = "Autoseparation completed successfully" });
            }
            catch (Exception ex)
            {
                await _batchLogService.LogTaskCompletionAsync(new BatchTaskCompletionRequest
                {
                    BatchId = batchId,
                    TaskType = "AUTO_SEPARATION",
                    Status = "FAILED",
                    TaskDescription = "Run Autoseparation",
                    ErrorMessage = ex.Message,
                    UserId = User.Identity?.Name ?? "system"
                });
                return StatusCode(500, new { message = $"Error running autoseparation: {ex.Message}" });
            }
        }

        [HttpGet("batch-verification-context/{batchId}")]
        public async Task<IActionResult> GetBatchVerificationContext(int batchId)
        {
            try
            {
                var context = await _verifyRepo.GetBatchVerificationContextAsync(batchId);
                return Ok(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch verification context for batch {BatchId}", batchId);
                return StatusCode(500, new { message = "Error retrieving batch context." });
            }
        }

        [HttpPost("bulk-save-index")]
        public async Task<IActionResult> BulkSaveIndexData([FromBody] BulkSaveIndexDto data)
        {
            if (data == null) return BadRequest("No data provided");
            
            try
            {
                var success = await _verifyRepo.BulkSaveIndexDataAsync(data);
                if (success)
                {
                    return Ok(new { message = "All documents saved successfully" });
                }
                return BadRequest("Failed to save some documents");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk saving index data for batch {BatchId}", data.BatchId);
                try { System.IO.File.WriteAllText("bulksave_error.txt", ex.ToString()); } catch {}
                return StatusCode(500, new { message = "Error bulk saving data." });
            }
        }
    }
}
