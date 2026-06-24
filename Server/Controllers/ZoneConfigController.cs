using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using System.ComponentModel.DataAnnotations;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/zoneconfig")]
    [Authorize]

    public class ZoneConfigController : ControllerBase
    {
        private readonly IConfigurationService _configService;
        private readonly IDocumentSampleRepository _documentSampleRepository;
        private readonly ILogger<ZoneConfigController> _logger;

        public ZoneConfigController(IConfigurationService configService, IDocumentSampleRepository documentSampleRepository, ILogger<ZoneConfigController> logger)
        {
            _configService = configService;
            _documentSampleRepository = documentSampleRepository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<ZoneConfig>>> GetAllZones()
        {
            try
            {
                var zones = await _configService.GetAllZonesAsync();
                return Ok(zones);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all zones");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("document/{documentTypeId}")]
        public async Task<ActionResult<List<ZoneConfig>>> GetZonesByDocumentType(int documentTypeId)
        {
            try
            {
                var zones = await _configService.GetZonesByDocumentTypeAsync(documentTypeId);
                return Ok(zones);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting zones for document type {DocumentTypeId}", documentTypeId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{zoneId}")]
        public async Task<ActionResult<ZoneConfig>> GetZoneById(int zoneId)
        {
            try
            {
                var zone = await _configService.GetZoneByIdAsync(zoneId);
                if (zone == null)
                {
                    return NotFound();
                }
                return Ok(zone);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting zone {ZoneId}", zoneId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost]
        public async Task<ActionResult<bool>> CreateZone([FromBody] CreateZoneRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var success = await _configService.CreateZoneAsync(request);
                if (success)
                {
                    return Ok(true);
                }
                return BadRequest("Failed to create zone");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating zone");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<bool>> UpdateZone(int id, [FromBody] UpdateZoneRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (id != request.ID)
                {
                    return BadRequest("Zone ID mismatch");
                }

                var success = await _configService.UpdateZoneAsync(request);
                if (success)
                {
                    return Ok(true);
                }
                return BadRequest("Failed to update zone");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating zone {ZoneId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{zoneId}")]
        public async Task<ActionResult<bool>> DeleteZone(int zoneId)
        {
            try
            {
                var success = await _configService.DeleteZoneAsync(zoneId);
                if (success)
                {
                    return Ok(true);
                }
                return BadRequest("Failed to delete zone");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting zone {ZoneId}", zoneId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("documentSample/{docTypeId}")]
        public async Task<ActionResult<DocumentSampleDto>> GetDocumentSample(int docTypeId)
        {
            try
            {
                var documentSample = await _documentSampleRepository.GetByDocTypeIdAsync(docTypeId);
                if (documentSample == null)
                {
                    return NotFound();
                }
                return Ok(documentSample);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting document sample for DocTypeId {DocTypeId}", docTypeId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("documentSample")]
        public async Task<ActionResult> SaveDocumentSample([FromBody] DocumentSampleDto documentSample)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var existing = await _documentSampleRepository.GetByDocTypeIdAsync(documentSample.DocTypeID);
                if (existing != null)
                {
                    // Update existing
                    var success = await _documentSampleRepository.UpdateAsync(documentSample);
                    if (success)
                    {
                        return Ok("Document sample updated successfully");
                    }
                    return BadRequest("Failed to update document sample");
                }
                else
                {
                    // Insert new
                    var id = await _documentSampleRepository.InsertAsync(documentSample);
                    if (id > 0)
                    {
                        return Ok("Document sample saved successfully");
                    }
                    return BadRequest("Failed to save document sample");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving document sample for DocTypeId {DocTypeId}", documentSample.DocTypeID);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("documentSample/{docTypeId}")]
        public async Task<ActionResult> DeleteDocumentSample(int docTypeId)
        {
            try
            {
                var success = await _documentSampleRepository.DeleteAsync(docTypeId);
                if (success)
                {
                    return Ok("Document sample deleted successfully");
                }
                return BadRequest("Failed to delete document sample");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document sample for DocTypeId {DocTypeId}", docTypeId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("template/{docTypeId}")]
        public async Task<ActionResult> UploadTemplate(int docTypeId,  IFormFile File)
        {
            try
            {
                if (File == null || File.Length == 0)
                {
                    return BadRequest("No file provided");
                }

                if (docTypeId <= 0)
                {
                    return BadRequest("Document Type ID is required");
                }

                var templateFolder = await _configService.GetConfigurationsValue("Templates Folder");
                if (string.IsNullOrEmpty(templateFolder))
                {
                    templateFolder = "./templates";
                }

                var templatePath = Path.Combine(Directory.GetCurrentDirectory(), templateFolder.TrimStart('.'));
                Directory.CreateDirectory(templatePath);

                var fileName = $"{docTypeId}_{DateTime.Now:yyyyMMddHHmmss}_{File.FileName}";
                var fullPath = Path.Combine(templatePath, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await File.CopyToAsync(stream);
                }

                // Update DocumentSample table with the new template file
                var documentSample = await _documentSampleRepository.GetByDocTypeIdAsync(docTypeId);
                if (documentSample == null)
                {
                    documentSample = new DocumentSampleDto
                    {
                        DocTypeID = docTypeId,
                        SampleFile = fileName
                    };
                    await _documentSampleRepository.InsertAsync(documentSample);
                }
                else
                {
                    documentSample.SampleFile = fileName;
                    await _documentSampleRepository.UpdateAsync(documentSample);
                }

                return Ok(new
                {
                    Success = true,
                    FileName = fileName,
                    Message = "Template uploaded successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading template for DocTypeId {DocTypeId}", docTypeId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("compare")]
        public async Task<ActionResult<TemplateComparisonResult>> CompareTemplate([FromForm] TemplateComparisonModel model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.ExistingTemplate) || model.File == null)
                {
                    return BadRequest("Existing template and comparison file are required");
                }

                var templateFolder = await _configService.GetConfigurationsValue("Templates Folder");
                if (string.IsNullOrEmpty(templateFolder))
                {
                    templateFolder = "./templates";
                }
                var templatePath = Path.Combine(Directory.GetCurrentDirectory(), templateFolder.TrimStart('.'));

                var existingTemplatePath = Path.Combine(templatePath, model.ExistingTemplate);

                if (!System.IO.File.Exists(existingTemplatePath))
                {
                    return BadRequest("Existing template file not found");
                }

                // Use a localized temp path within the application directory for better security (S5445 remediation)
                var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Temp");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, Path.GetRandomFileName());

                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await model.File.CopyToAsync(stream);
                }

                try
                {
                    // For now, implement a basic comparison (in a real implementation, you might want to use image processing)
                    // Here we'll just compare file sizes as a simple similarity check
                    var existingFileInfo = new System.IO.FileInfo(existingTemplatePath);
                    var comparisonFileInfo = new System.IO.FileInfo(tempPath);
                    
                    // Calculate a basic similarity based on file size
                    var sizeDiff = Math.Abs(existingFileInfo.Length - comparisonFileInfo.Length);
                    var maxSize = Math.Max(existingFileInfo.Length, comparisonFileInfo.Length);
                    var sizeSimilarity = maxSize > 0 ? (1.0 - (double)sizeDiff / maxSize) * 100 : 100.0;
                    
                    var result = new TemplateComparisonResult
                    {
                        Success = true,
                        Similarity = Math.Round(sizeSimilarity, 2),
                        Message = $"Template comparison completed with {sizeSimilarity:F2}% similarity"
                    };
                    
                    return Ok(result);
                }
                finally
                {
                    // Clean up temporary file
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing templates");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
