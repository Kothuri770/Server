using Server.Models;

namespace Server.Services.DMS
{
    public interface IDmsConnector
    {
        Task<bool> ConnectAsync(DmsConfigDto config);
        Task<bool> UploadDocumentAsync(DmsConfigDto config, string localFilePath, string documentName, Dictionary<string, string> metadata);
        Task<bool> TestConnectionAsync(DmsConfigDto config);
        string GetConnectorType();
    }
}