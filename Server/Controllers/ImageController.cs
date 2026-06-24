using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using Server.Services.Scanner;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/image")]
    [Authorize]
    public class ImageController : ControllerBase
    {
        private readonly IImageTransformationService _transformService;
        private readonly IFileStorageService _fileStorageService;
        private readonly IBatchRepository _batchRepository;
        private readonly ILogger<ImageController> _logger;

        public ImageController(
            IImageTransformationService transformService,
            IFileStorageService fileStorageService,
            IBatchRepository batchRepository,
            ILogger<ImageController> logger)
        {
            _transformService = transformService;
            _fileStorageService = fileStorageService;
            _batchRepository = batchRepository;
            _logger = logger;
        }

        [HttpPost("transform")]
        public async Task<IActionResult> TransformImage([FromBody] TransformRequest request)
        {
            try
            {
                var result = await _transformService.RotateImageAsync(
                    request.FileName, request.BatchId, request.Degrees);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transforming image");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteImage([FromBody] DeleteImageRequest request)
        {
            try
            {
                var batchPath = await _fileStorageService.GetBatchPathAsync((int)request.BatchId);
                
                // Delete the file from the file system
                var filePath = Path.Combine(batchPath, request.FileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    
                    // Also delete the thumbnail if it exists
                    var thumbPath = Path.Combine(batchPath, $"thumb_{request.FileName}");
                    if (System.IO.File.Exists(thumbPath))
                    {
                        System.IO.File.Delete(thumbPath);
                    }
                }
                
                // Update the batch detail status in the database
                await _batchRepository.DeleteBatchDetailAsync(request.FileName, request.BatchId);
                
                // Also update the document status in the Document table
                await _batchRepository.UpdateDocumentStatusAsync(request.FileName, request.BatchId, "D");
                
                return Ok(new { Message = "Image deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("replace")]
        public async Task<IActionResult> ReplaceImage([FromBody] ReplaceImageRequest request)
        {
            try
            {
                var batchPath = await _fileStorageService.GetBatchPathAsync((int)request.BatchId);
                var filePath = Path.Combine(batchPath, request.FileName);

                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound(new { Message = "Image file not found" });
                }

                // Decode base64 and overwrite the original file
                var imageBytes = Convert.FromBase64String(request.Base64Data);
                await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

                // Also update thumbnail if it exists
                var thumbPath = Path.Combine(batchPath, $"thumb_{request.FileName}");
                if (System.IO.File.Exists(thumbPath))
                {
                    await System.IO.File.WriteAllBytesAsync(thumbPath, imageBytes);
                }

                _logger.LogInformation("Image replaced successfully: {FileName} in batch {BatchId}", request.FileName, request.BatchId);
                return Ok(new { Message = "Image replaced successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replacing image");
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class TransformRequest
    {
        public string FileName { get; set; } = string.Empty;
        public long BatchId { get; set; }
        public int Degrees { get; set; }
    }

    public class DeleteImageRequest
    {
        public string FileName { get; set; } = string.Empty;
        public long BatchId { get; set; }
    }

    public class ReplaceImageRequest
    {
        public long BatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Base64Data { get; set; } = string.Empty;
    }

    public class ImageResponse
    {
        public string Url { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
    }

    public class TransformResult
    {
        public string NewUrl { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}
