using Server.Models;

namespace Server.Services;

public interface IOcrConnectorService
{
    Task<IEnumerable<OcrProviderDto>> GetAllOcrProvidersAsync();
    Task<IEnumerable<OcrConnectorDto>> GetAllOcrConnectorsAsync();
    Task<OcrConnectorDto?> GetDefaultOcrConnectorAsync();
    Task<OcrConnectorDto?> GetOcrConnectorByApplicationIdAsync(int applicationId);
    Task<OcrConnectorDto?> GetOcrConnectorByIdAsync(int id);
    Task<OcrConnectorDto> CreateOcrConnectorAsync(OcrConnectorDto connectorDto);
    Task<OcrConnectorDto> UpdateOcrConnectorAsync(int id, OcrConnectorDto connectorDto);
    Task<bool> DeleteOcrConnectorAsync(int id);
    Task<bool> SetDefaultOcrConnectorAsync(int id);
    Task<OcrConfigurationDto?> GetOcrConfigurationAsync(string configName);
    Task<bool> UpdateOcrConfigurationAsync(string configName, string configValue);
    Task<bool> UpdateStepStatusForOcrModeAsync(string ocrMode);
}