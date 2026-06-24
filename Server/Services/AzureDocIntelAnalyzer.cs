using Azure;
using Azure.AI.DocumentIntelligence;
using System.Text.Json;

namespace Server.Services
{
    public class AzureDocIntelAnalyzer
    {
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _modelID;
        private readonly JsonSerializerOptions _jsonOptions;

        public AzureDocIntelAnalyzer(string endpoint, string apiKey, string modelID)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _modelID = modelID ?? throw new ArgumentNullException(nameof(modelID));

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task<bool> AnalyzeAndSaveAsync(string inputPath, string outputDirectory)
        {
            try
            {
                // Validation
                if (!System.IO.File.Exists(inputPath))
                    throw new FileNotFoundException($"File not found: {inputPath}");

                Directory.CreateDirectory(outputDirectory);
                string baseName = Path.GetFileNameWithoutExtension(inputPath);

                // ✅ FIXED: AzureKeyCredential also needs disposal
                var credential = new AzureKeyCredential(_apiKey);
                var client = new DocumentIntelligenceClient(new Uri(_endpoint), credential);
                
                byte[] imageBytes = await System.IO.File.ReadAllBytesAsync(inputPath);

                var options = new AnalyzeDocumentOptions(_modelID, BinaryData.FromBytes(imageBytes))
                {
                    Features = { DocumentAnalysisFeature.KeyValuePairs }
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                Operation<AnalyzeResult> operation = await client.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    options,
                    cts.Token);


                AnalyzeResult result = operation.Value;

                if (result?.Pages == null || result.Pages.Count == 0)
                {
                    return false;
                }

                var output = new
                {
                    Metadata = new
                    {
                        InputFile = Path.GetFileName(inputPath),
                        AnalyzedAt = DateTime.UtcNow,
                        TotalPages = result.Pages.Count,
                        TableCount = result.Tables?.Count ?? 0,
                        KvpCount = result.KeyValuePairs?.Count ?? 0
                    },
                    // ✅ FIXED: Check for null before processing
                    Tables = result.Tables != null ? ProcessTables(result.Tables) : new List<dynamic>(),
                    // ✅ FIXED: Use BoundingRegions instead of Polygon
                    KeyValuePairs = result.KeyValuePairs != null ? ProcessKeyValuePairs(result.KeyValuePairs) : new List<dynamic>(),
                    // ✅ FIXED: Simplified OCR (lines don't have Words directly)
                    OcrData = ProcessOcrData(result.Pages)
                };

                string outputPath = Path.Combine(outputDirectory, $"{baseName}_analysis.json");
                await System.IO.File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(output, _jsonOptions));


                return true;
            }
            catch (RequestFailedException ex)
            {
                // Re-throw to be caught by the calling service's logger
                throw new Exception($"Azure Request Failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Azure Analysis Error: {ex.Message}", ex);
            }
        }

        private List<dynamic> ProcessTables(IReadOnlyList<DocumentTable> tables)
        {
            if (tables == null) return new List<dynamic>();

            return tables.Select((table, idx) => new
            {
                TableIndex = idx,
                RowCount = table.RowCount,
                ColumnCount = table.ColumnCount,
                Cells = table.Cells?.Where(cell => cell != null).Select(cell => (dynamic)new
                {
                    Row = cell.RowIndex,
                    Col = cell.ColumnIndex,
                    Content = cell.Content ?? "",
                    Kind = cell.Kind?.ToString() ?? "",
                    SpanCount = (cell.RowSpan ?? 1) * (cell.ColumnSpan ?? 1)
                }).ToList() ?? new List<dynamic>()
            }).ToList<dynamic>();
        }

        private List<dynamic> ProcessKeyValuePairs(IReadOnlyList<DocumentKeyValuePair> kvps)
        {
            if (kvps == null) return new List<dynamic>();

            return kvps.Select((kvp, idx) => new
            {
                PairIndex = idx,
                Key = new
                {
                    Text = kvp.Key?.Content ?? "",
                    Regions = kvp.Key?.BoundingRegions?.Select(br => (dynamic)new
                    {
                        Page = br.PageNumber,
                        Polygon = br.Polygon?.ToArray()
                    }).ToArray() ?? Array.Empty<dynamic>()
                },
                Value = kvp.Value != null ? new
                {
                    Text = kvp.Value.Content ?? "",
                    Confidence = kvp.Confidence,
                    Regions = kvp.Value.BoundingRegions?.Select(br => (dynamic)new
                    {
                        Page = br.PageNumber,
                        Polygon = br.Polygon?.ToArray()
                    }).ToArray() ?? Array.Empty<dynamic>()
                } : null,
                Confidence = kvp.Confidence
            }).ToList<dynamic>();
        }

        private List<dynamic> ProcessOcrData(IReadOnlyList<DocumentPage> pages)
        {
            if (pages == null) return new List<dynamic>();

            return pages.Select(page => new
            {
                PageNumber = page.PageNumber,
                Angle = page.Angle,
                Dimensions = new { Width = page.Width, Height = page.Height, Unit = page.Unit?.ToString() },
                // ✅ FIXED: Lines don't have Words property directly
                Lines = page.Lines?.Select(line => new
                {
                    Text = line.Content ?? "",
                    // Polygon is an array of floats
                    Polygon = line.Polygon?.ToArray()
                }).ToArray() ?? Array.Empty<dynamic>(),
                // ✅ NEW: Get words directly from page
                Words = page.Words?.Select(word => new
                {
                    Text = word.Content ?? "",
                    Confidence = word.Confidence,
                    Polygon = word.Polygon?.ToArray()
                }).ToArray() ?? Array.Empty<dynamic>()
            }).ToList<dynamic>();
        }
    }
}