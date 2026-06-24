using Microsoft.AspNetCore.Mvc;
using Server.Services.Configuration;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TemplateController : ControllerBase
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<TemplateController> _logger;

        public TemplateController(IConfigurationService configService, ILogger<TemplateController> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        [HttpGet("{fileName}")]
        public async Task<IActionResult> GetTemplate(string fileName)
        {
            try
            {
                // Get the templates folder path from configuration
                var templateFolder = await _configService.GetConfigurationsValue("Templates Folder");
                if (string.IsNullOrEmpty(templateFolder))
                {
                    templateFolder = "./templates"; // Default fallback
                }

                // Ensure the path is safe
                var templatePath = Path.Combine(Directory.GetCurrentDirectory(), templateFolder.TrimStart('.'));
                var filePath = Path.Combine(templatePath, fileName);

                // Security check to prevent directory traversal
                var fullPath = Path.GetFullPath(filePath);
                var templateDirPath = Path.GetFullPath(templatePath);

                if (!fullPath.StartsWith(templateDirPath, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Invalid file path");
                }

                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound();
                }

                var fileExtension = Path.GetExtension(filePath);
                var contentType = GetContentType(fileExtension);

                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                return File(fileBytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving template file {FileName}", fileName);
                return StatusCode(500, "Error retrieving template file");
            }
        }

        private string GetContentType(string extension)
        {
            return extension.ToLowerInvariant() switch
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
    }
}
