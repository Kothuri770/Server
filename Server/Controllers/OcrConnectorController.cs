using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;
using System.Text.Json;

namespace Server.Controllers;

[ApiController]
[Route("api/ocr-connector")]
[Authorize]
public class OcrConnectorController : ControllerBase
{
    private readonly IOcrConnectorService _ocrConnectorService;
    private readonly ILogger<OcrConnectorController> _logger;

    public OcrConnectorController(IOcrConnectorService ocrConnectorService, ILogger<OcrConnectorController> logger)
    {
        _ocrConnectorService = ocrConnectorService;
        _logger = logger;
    }

    [HttpGet("providers")]
    public async Task<ActionResult<IEnumerable<OcrProviderDto>>> GetOcrProviders()
    {
        try
        {
            var providers = await _ocrConnectorService.GetAllOcrProvidersAsync();
            return Ok(providers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OCR providers");
            return StatusCode(500, "An error occurred while retrieving OCR providers");
        }
    }

    [HttpGet("connectors")]
    public async Task<ActionResult<IEnumerable<OcrConnectorDto>>> GetOcrConnectors()
    {
        try
        {
            var connectors = await _ocrConnectorService.GetAllOcrConnectorsAsync();
            return Ok(connectors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OCR connectors");
            return StatusCode(500, "An error occurred while retrieving OCR connectors");
        }
    }

    [HttpGet("connectors/default")]
    public async Task<ActionResult<OcrConnectorDto>> GetDefaultOcrConnector()
    {
        try
        {
            var connector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
            return Ok(connector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving default OCR connector");
            return StatusCode(500, "An error occurred while retrieving default OCR connector");
        }
    }

    [HttpGet("connectors/{id}")]
    public async Task<ActionResult<OcrConnectorDto>> GetOcrConnector(int id)
    {
        try
        {
            var connector = await _ocrConnectorService.GetOcrConnectorByIdAsync(id);
            if (connector == null)
            {
                return NotFound($"OCR connector with ID {id} not found");
            }
            return Ok(connector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OCR connector with ID {Id}", id);
            return StatusCode(500, "An error occurred while retrieving OCR connector");
        }
    }

    [HttpPost("connectors")]
    public async Task<ActionResult<OcrConnectorDto>> CreateOcrConnector([FromBody] OcrConnectorDto connectorDto)
    {
        try
        {
            if (connectorDto == null)
            {
                return BadRequest("Connector data is required");
            }

            var createdConnector = await _ocrConnectorService.CreateOcrConnectorAsync(connectorDto);
            return CreatedAtAction(nameof(GetOcrConnector), new { id = createdConnector.Id }, createdConnector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OCR connector");
            return StatusCode(500, "An error occurred while creating OCR connector");
        }
    }

    [HttpPut("connectors/{id}")]
    public async Task<ActionResult<OcrConnectorDto>> UpdateOcrConnector(int id, [FromBody] OcrConnectorDto connectorDto)
    {
        try
        {
            if (connectorDto == null)
            {
                return BadRequest("Connector data is required");
            }

            var updatedConnector = await _ocrConnectorService.UpdateOcrConnectorAsync(id, connectorDto);
            if (updatedConnector == null)
            {
                return NotFound($"OCR connector with ID {id} not found");
            }
            return Ok(updatedConnector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating OCR connector with ID {Id}", id);
            return StatusCode(500, "An error occurred while updating OCR connector");
        }
    }

    [HttpDelete("connectors/{id}")]
    public async Task<ActionResult<bool>> DeleteOcrConnector(int id)
    {
        try
        {
            var result = await _ocrConnectorService.DeleteOcrConnectorAsync(id);
            if (!result)
            {
                return NotFound($"OCR connector with ID {id} not found");
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting OCR connector with ID {Id}", id);
            return StatusCode(500, "An error occurred while deleting OCR connector");
        }
    }

    [HttpPost("connectors/{id}/set-default")]
    public async Task<ActionResult<bool>> SetDefaultOcrConnector(int id)
    {
        try
        {
            var result = await _ocrConnectorService.SetDefaultOcrConnectorAsync(id);
            if (!result)
            {
                return NotFound($"OCR connector with ID {id} not found");
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default OCR connector with ID {Id}", id);
            return StatusCode(500, "An error occurred while setting default OCR connector");
        }
    }

    [HttpGet("configuration/{configName}")]
    public async Task<ActionResult<OcrConfigurationDto>> GetOcrConfiguration(string configName)
    {
        try
        {
            var config = await _ocrConnectorService.GetOcrConfigurationAsync(configName);
            if (config == null)
            {
                return NotFound($"OCR configuration '{configName}' not found");
            }
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OCR configuration '{ConfigName}'", configName);
            return StatusCode(500, "An error occurred while retrieving OCR configuration");
        }
    }

    [HttpPut("configuration/{configName}")]
    public async Task<ActionResult<bool>> UpdateOcrConfiguration(string configName, [FromBody] JsonElement configValue)
    {
        try
        {
            string value = configValue.ValueKind == JsonValueKind.String 
                ? configValue.GetString() ?? string.Empty 
                : configValue.GetRawText();
                
            var result = await _ocrConnectorService.UpdateOcrConfigurationAsync(configName, value);
            if (!result)
            {
                return NotFound($"OCR configuration '{configName}' not found");
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating OCR configuration '{ConfigName}'", configName);
            return StatusCode(500, "An error occurred while updating OCR configuration");
        }
    }

    [HttpPost("ocr-mode")]
    public async Task<ActionResult<bool>> UpdateOcrMode([FromBody] UpdateOcrModeRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrEmpty(request.OcrMode))
            {
                return BadRequest("OCR mode is required");
            }

            var result = await _ocrConnectorService.UpdateStepStatusForOcrModeAsync(request.OcrMode);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating OCR mode to '{OcrMode}'", request?.OcrMode);
            return StatusCode(500, "An error occurred while updating OCR mode");
        }
    }

    [HttpGet("connectors/application/{applicationId}")]
    public async Task<ActionResult<OcrConnectorDto>> GetOcrConnectorByApplicationId(int applicationId)
    {
        try
        {
            var connector = await _ocrConnectorService.GetOcrConnectorByApplicationIdAsync(applicationId);
            return Ok(connector);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OCR connector for Application ID {AppId}", applicationId);
            return StatusCode(500, "An error occurred while retrieving OCR connector");
        }
    }
}