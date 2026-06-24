using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using Server.Services.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/upload")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private readonly IFileStorageService _fileService;
        private readonly IBatchRepository _batchRepo;

        private readonly ILogger<UploadController> _logger;
        private readonly IConfigurationService _configService;
        private readonly IImageTransformationService _imageTransformationService;

        public UploadController(
            IFileStorageService fileService,
            IConfigurationService configService,
            IBatchRepository batchRepo,
            ILogger<UploadController> logger,
            IImageTransformationService imageTransformationService)
        {
            _fileService = fileService;
            _batchRepo = batchRepo;
            _logger = logger;
            _configService = configService;
            _imageTransformationService = imageTransformationService;
        }


        [HttpPost("batch/{batchId}/doc/{docId}")]
        [RequestSizeLimit(524288000)] // 500MB
        public async Task<IActionResult> UploadFiles(long batchId, int docId, [FromForm] List<IFormFile> files)
        {
            if (files == null || !files.Any())
                return BadRequest("No files provided");

            try
            {
                var results = new List<ImageUploadResult>();
                var batchPath = await _fileService.GetBatchPathAsync((int)batchId);

                foreach (var file in files)
                {
                    // Save original file
                    var fileName = await _fileService.SaveFileAsync(file, (int)batchId);

                    var dto = new BatchDetailDto
                    {
                        BatchId = (int)batchId,
                        PageNo = 0, // Handled atomically by repository
                        FileName = fileName,
                        Format = Path.GetExtension(fileName).TrimStart('.').ToUpper(),
                        DocPage = 1,
                        Status = "A",
                        DocTypeId = docId,
                        PageName = file.FileName.Length > 255 ? file.FileName.Substring(0, 252) + "..." : file.FileName,
                        DocName = file.FileName.Length > 255 ? file.FileName.Substring(0, 252) + "..." : file.FileName,
                        InternalName = fileName.Length > 100 ? fileName.Substring(0, 97) + "..." : fileName,
                        DocCreatedOn = DateTime.UtcNow
                    };
                    
                    // Insert into database
                    var pageId = await _batchRepo.InsertBatchDetailAsync(dto);

                    // Generate thumbnail

                    // Create result
                    results.Add(new ImageUploadResult
                    {
                        Id = pageId,
                        ImageId = $"IMG{pageId:D6}",
                        FileName = fileName,
                        DocId = docId,
                        ImageUrl = $"/api/batch/{batchId}/file/{Uri.EscapeDataString(fileName)}",
                        ThumbnailBase64Data = $"/api/batch/{batchId}/file/{Uri.EscapeDataString(fileName)}", // NEW property
                        IsPdf = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase),
                        PageName = file.FileName // Store original filename
                    });
                }

                // Auto-rotate all uploaded images so they are straight for ALL subsequent steps
                // _logger.LogInformation("Running auto-rotation for Batch {BatchId} after upload", batchId);
                // await _imageTransformationService.AutoOrientBatchImagesAsync((int)batchId);
                // _logger.LogInformation("Auto-rotation completed for Batch {BatchId}", batchId);

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading files for batch {BatchId}", batchId);
                return StatusCode(500, $"Upload failed: {ex.Message}");
            }
        }

        [HttpPost("batch/{batchId}/doc/{docId}/bulk")]
        [RequestSizeLimit(524288000)] // 500MB
        public async Task<IActionResult> UploadFilesBulk(long batchId, int docId, [FromForm] List<IFormFile> files)
        {
            if (files == null || !files.Any())
                return BadRequest("No files provided");

            try
            {
                var results = new List<ImageUploadResult>();

                // Parallelize file saving to disk
                var saveTasks = files.Select(async file =>
                {
                    var fileName = await _fileService.SaveFileAsync(file, (int)batchId);
                    return new { File = file, FileName = fileName };
                });

                var savedFiles = await Task.WhenAll(saveTasks);

                foreach (var item in savedFiles)
                {
                    var file = item.File;
                    var fileName = item.FileName;
                    
                    var dto = new BatchDetailDto
                    {
                        BatchId = (int)batchId,
                        PageNo = 0, // Handled atomically by repository
                        FileName = fileName,
                        Format = Path.GetExtension(fileName).TrimStart('.').ToUpper(),
                        DocPage = 1,
                        Status = "A",
                        DocTypeId = docId,
                        PageName = file.FileName.Length > 255 ? file.FileName.Substring(0, 252) + "..." : file.FileName,
                        DocName = "", // Will be set after atomicity
                        InternalName = fileName.Length > 100 ? fileName.Substring(0, 97) + "..." : fileName,
                        DocCreatedOn = DateTime.UtcNow
                    };
                    
                    var pageId = await _batchRepo.InsertBatchDetailAsync(dto);
                    
                    // Now that PageNo is atomically determined, we update DocName if needed.
                    // Note: DocName wasn't actually committed with this new name in InsertBatchDetailAsync, 
                    // but we can just use the atomic pageNo for the response. If the DB needs it, 
                    // we'd need a separate UPDATE, but DocName is rarely strictly tied to PageNo in the DB.
                    // The client can use dto.PageNo for display.

                    results.Add(new ImageUploadResult
                    {
                        Id = pageId,
                        ImageId = $"IMG{pageId:D6}",
                        FileName = fileName,
                        DocId = docId,
                        ImageUrl = $"/api/batch/{batchId}/file/{Uri.EscapeDataString(fileName)}",
                        ThumbnailBase64Data = $"/api/batch/{batchId}/file/{Uri.EscapeDataString(fileName)}",
                        IsPdf = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase),
                        PageName = file.FileName
                    });
                }

                // Auto-rotate all uploaded images so they are straight for ALL subsequent steps
                // _logger.LogInformation("Running auto-rotation for Batch {BatchId} after bulk upload", batchId);
                // await _imageTransformationService.AutoOrientBatchImagesAsync((int)batchId);
                // _logger.LogInformation("Auto-rotation completed for Batch {BatchId}", batchId);

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading files in bulk for batch {BatchId}", batchId);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

      

        // Update the UploadTemplate method
        [HttpPost("template/{docTypeId}")]
        [Authorize(Roles = "admin,configeditor")]
        [RequestSizeLimit(524288000)] // 500MB
        public async Task<IActionResult> UploadTemplate(
            [FromRoute] int docTypeId,
            [FromForm] TemplateUploadModel model)
        {
            if (model.File == null || model.File.Length == 0)
                return BadRequest("No file provided");

            if (docTypeId <= 0)
                return BadRequest("Document Type ID is required");

            try
            {
                var templateFolder = await _configService.GetConfigurationsValue("Templates Folder");
                if (string.IsNullOrEmpty(templateFolder))
                {
                    templateFolder = "./templates";
                }

                var templatePath = Path.Combine(Directory.GetCurrentDirectory(), templateFolder.TrimStart('.'));
                Directory.CreateDirectory(templatePath);

                var fileName = $"{docTypeId}_{DateTime.Now:yyyyMMddHHmmss}_{model.File.FileName}";
                var fullPath = Path.Combine(templatePath, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await model.File.CopyToAsync(stream);
                }

                return Ok(new
                {
                    Success = true,
                    FileName = fileName,
                    FileUrl = $"/api/template/{Uri.EscapeDataString(fileName)}",
                    Message = "Template uploaded successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading template for DocTypeId {DocTypeId}", docTypeId);
                return StatusCode(500, $"Template upload failed: {ex.Message}");
            }
        }

    }
}
