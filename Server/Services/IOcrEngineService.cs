using Server.Models;

namespace Server.Services
{
    public interface IOcrEngineService
    {
        /// <summary>
        /// Processes a single document using the specified OCR provider.
        /// </summary>
        Task ProcessDocumentAsync(string providerName, DocumentModel document, List<PageModel> documentPages, int batchId, Dictionary<string, object>? configData = null);

        /// <summary>
        /// Processes a specific zone within a document using Tesseract.
        /// </summary>
        Task ProcessZoneWithTesseractAsync(DocumentModel document, List<PageModel> documentPages, ZoneDto zone);
    }
}
