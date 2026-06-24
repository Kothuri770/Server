using Google.Apis.Auth.OAuth2;
using Google.Cloud.DocumentAI.V1;
using Grpc.Auth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Server.Services
{
    public class GoogleDocAiAnalyzer
    {
        private readonly string _endpoint;
        private readonly string _processorId;
        private readonly string _apiKey;
        private readonly JsonSerializerOptions _jsonOptions;

        public GoogleDocAiAnalyzer(string endpoint, string apiKey, string processorId)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _processorId = processorId ?? throw new ArgumentNullException(nameof(processorId));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task<bool> AnalyzeAndSaveAsync(string inputPath, string outputDirectory)
        {
            try
            {
                if (!File.Exists(inputPath))
                    throw new FileNotFoundException($"File not found: {inputPath}");

                Directory.CreateDirectory(outputDirectory);
                string baseName = Path.GetFileNameWithoutExtension(inputPath);

               
                byte[] fileBytes = await File.ReadAllBytesAsync(inputPath);
                GoogleCredential credential = GoogleCredential.FromJson(_apiKey)
               .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
                var inputConfig = new RawDocument
                {
                    Content = Google.Protobuf.ByteString.CopyFrom(fileBytes),
                    MimeType = GetMimeType(inputPath)
                };

                var processorName = $"projects/{GetProjectIdFromEndpoint(_endpoint)}/locations/{GetLocationFromEndpoint(_endpoint)}/processors/{_processorId}";
                var client = new DocumentProcessorServiceClientBuilder
                {
                    Endpoint = $"{GetLocationFromEndpoint(_endpoint)}-documentai.googleapis.com",
                    ChannelCredentials = credential.ToChannelCredentials()
                }.Build();
                var request = new ProcessRequest
                {
                    Name = processorName,
                    RawDocument = inputConfig
                };

                var response = await client.ProcessDocumentAsync(request);
                var document = response.Document;

                if (document.Pages == null || document.Pages.Count == 0)
                {
                    return false;
                }

                var output = new
                {
                    Metadata = new
                    {
                        InputFile = Path.GetFileName(inputPath),
                        AnalyzedAt = DateTime.UtcNow,
                        TotalPages = document.Pages.Count,
                        TableCount = document.Pages.Sum(p => p.Tables.Count),
                        KvpCount = document.Pages.Sum(p => p.FormFields.Count)
                    },
                    Tables = ProcessTables(document),
                    KeyValuePairs = ProcessKeyValuePairs(document),
                    OcrData = ProcessOcrData(document)
                };

                string outputPath = Path.Combine(outputDirectory, $"{baseName}_analysis.json");
                await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(output, _jsonOptions));


                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GetMimeType(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".tiff" or ".tif" => "image/tiff",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                _ => "application/pdf"
            };
        }

        private string GetProjectIdFromEndpoint(string endpoint)
        {
            // Parse the endpoint format: "projectid:lively-oxide-485107-d5,locations:us"
            if (string.IsNullOrEmpty(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty");

            var parts = endpoint.Split(',');
            foreach (var part in parts)
            {
                if (part.StartsWith("projectid:"))
                {
                    return part.Substring("projectid:".Length);
                }
            }

            throw new ArgumentException("Invalid endpoint format - projectid not found");
        }

        private string GetLocationFromEndpoint(string endpoint)
        {
            // Parse the endpoint format: "projectid:lively-oxide-485107-d5,locations:us"
            if (string.IsNullOrEmpty(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty");

            var parts = endpoint.Split(',');
            foreach (var part in parts)
            {
                if (part.StartsWith("locations:"))
                {
                    return part.Substring("locations:".Length);
                }
            }

            throw new ArgumentException("Invalid endpoint format - locations not found");
        }

        private List<dynamic> ProcessTables(Document document)
        {
            var allTables = new List<dynamic>();
            int tableIndex = 0;

            foreach (var page in document.Pages)
            {
                foreach (var table in page.Tables)
                {
                    var cells = new List<dynamic>();
                    int currentRow = 0;

                    if (table.HeaderRows != null)
                    {
                        foreach (var row in table.HeaderRows)
                        {
                            int currentCol = 0;
                            if (row.Cells != null)
                            {
                                foreach (var cell in row.Cells)
                                {
                                    cells.Add(CreateCell(cell, document, currentRow, currentCol, "header"));
                                    currentCol++;
                                }
                            }
                            currentRow++;
                        }
                    }

                    if (table.BodyRows != null)
                    {
                        foreach (var row in table.BodyRows)
                        {
                            int currentCol = 0;
                            if (row.Cells != null)
                            {
                                foreach (var cell in row.Cells)
                                {
                                    cells.Add(CreateCell(cell, document, currentRow, currentCol, "body"));
                                    currentCol++;
                                }
                            }
                            currentRow++;
                        }
                    }

                    allTables.Add(new
                    {
                        TableIndex = tableIndex++,
                        RowCount = currentRow,
                        ColumnCount = cells.Any() ? cells.Max(c => (int)c.Col) + 1 : 0,
                        Cells = cells
                    });
                }
            }
            return allTables;
        }

        private dynamic CreateCell(Document.Types.Page.Types.Table.Types.TableCell cell, Document document, int row, int col, string kind)
        {
            return new
            {
                Row = row,
                Col = col,
                Content = GetText(cell.Layout, document),
                Kind = kind,
                RowSpan = cell.RowSpan,
                ColSpan = cell.ColSpan
            };
        }

        private List<dynamic> ProcessKeyValuePairs(Document document)
        {
            var allKvps = new List<dynamic>();
            int kvpIndex = 0;

            foreach (var page in document.Pages)
            {
                foreach (var field in page.FormFields)
                {
                    // FIX: FieldName and FieldValue are already Layout objects
                    var keyLayout = field.FieldName;
                    var keyValue = field.FieldValue;

                    if (keyLayout == null) continue;

                    allKvps.Add(new
                    {
                        PairIndex = kvpIndex++,
                        Key = new
                        {
                            Text = GetText(keyLayout, document),
                            Confidence = keyLayout.Confidence,
                            Regions = GetRegions(keyLayout, page.PageNumber)
                        },
                        Value = keyValue != null ? new
                        {
                            Text = GetText(keyValue, document),
                            Confidence = keyValue.Confidence,
                            Regions = GetRegions(keyValue, page.PageNumber)
                        } : null,
                        keyValue.Confidence
                    });
                }
            }
            return allKvps;
        }

        private List<dynamic> ProcessOcrData(Document document)
        {
            return document.Pages.Select(page => new
            {
                PageNumber = page.PageNumber,
                Dimensions = new
                {
                    Width = page.Dimension?.Width ?? 0,
                    Height = page.Dimension?.Height ?? 0,
                    Unit = page.Dimension?.Unit?.ToString() ?? "unknown"
                },
                Lines = page.Lines?.Select(line => new
                {
                    Text = GetText(line.Layout, document),
                    Polygon = GetPolygon(line.Layout)
                }).Cast<dynamic>().ToList() ?? new List<dynamic>(),
                Words = page.Tokens?.Select(token => new
                {
                    Text = GetText(token.Layout, document),
                    Confidence = token.Layout.Confidence,
                    // Token does not have Confidence property
                    Polygon = GetPolygon(token.Layout)
                }).Cast<dynamic>().ToList() ?? new List<dynamic>()
            }).ToList<dynamic>();
        }

        private double[] GetPolygon(Document.Types.Page.Types.Layout layout)
        {
            var vertices = layout?.BoundingPoly?.NormalizedVertices;
            if (vertices == null || vertices.Count == 0)
                return Array.Empty<double>();

            return vertices.SelectMany(v => new double[] { v.X, v.Y }).ToArray();
        }

        private dynamic[] GetRegions(Document.Types.Page.Types.Layout layout, int pageNumber)
        {
            var vertices = layout?.BoundingPoly?.NormalizedVertices;
            if (vertices == null || vertices.Count == 0)
                return Array.Empty<dynamic>();

            return vertices.Select(v => new
            {
                Page = pageNumber,
                Polygon = new[] { v.X, v.Y }
            }).ToArray();
        }

        private string GetText(Document.Types.Page.Types.Layout layout, Document document)
        {
            if (layout?.TextAnchor?.TextSegments?.Count > 0)
            {
                var segment = layout.TextAnchor.TextSegments[0];
                int start = (int)segment.StartIndex;
                int end = (int)segment.EndIndex;

                if (start >= 0 && end > start && end <= document.Text.Length)
                {
                    return document.Text.Substring(start, end - start);
                }
            }
            return "";
        }
    }
}