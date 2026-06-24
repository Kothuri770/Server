using Amazon.Textract;
using Amazon.Textract.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Services
{
    public class AmazonTextractAnalyzer
    {
        private readonly string _region;
        private readonly string _accessKey;
        private readonly string _secretKey;
        private readonly JsonSerializerOptions _jsonOptions;

        public AmazonTextractAnalyzer(string region, string accessKey, string secretKey)
        {
            _region = region ?? throw new ArgumentNullException(nameof(region));
            _accessKey = accessKey ?? throw new ArgumentNullException(nameof(accessKey));
            _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
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
                if (!System.IO.File.Exists(inputPath))
                    throw new FileNotFoundException($"File not found: {inputPath}");

                Directory.CreateDirectory(outputDirectory);
                string baseName = Path.GetFileNameWithoutExtension(inputPath);

                var config = new AmazonTextractConfig { RegionEndpoint = GetRegionEndpoint(_region) };
                using var client = new AmazonTextractClient(_accessKey, _secretKey, config);

                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(inputPath);
                var request = new AnalyzeDocumentRequest
                {
                    Document = new Amazon.Textract.Model.Document { Bytes = new MemoryStream(fileBytes) },
                    FeatureTypes = new List<string> { "TABLES", "FORMS" }
                };

                var response = await client.AnalyzeDocumentAsync(request);

                if (response.Blocks == null || !response.Blocks.Any())
                {
                    return false;
                }

                var output = new
                {
                    Metadata = new
                    {
                        InputFile = Path.GetFileName(inputPath),
                        AnalyzedAt = DateTime.UtcNow,
                        TotalPages = 1,
                        TableCount = response.Blocks.Count(b => b.BlockType == BlockType.TABLE),
                        KvpCount = response.Blocks.Count(b => b.BlockType == BlockType.KEY_VALUE_SET && b.EntityTypes.Contains("KEY"))
                    },
                    Tables = ProcessTables(response.Blocks),
                    KeyValuePairs = ProcessKeyValuePairs(response.Blocks),
                    OcrData = ProcessOcrData(response.Blocks)
                };

                string outputPath = Path.Combine(outputDirectory, $"{baseName}_analysis.json");
                await System.IO.File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(output, _jsonOptions));


                return true;
            }
            catch
            {
                return false;
            }
        }

        private Amazon.RegionEndpoint GetRegionEndpoint(string regionName) => regionName switch
        {
            "us-east-1" => Amazon.RegionEndpoint.USEast1,
            "us-east-2" => Amazon.RegionEndpoint.USEast2,
            "us-west-1" => Amazon.RegionEndpoint.USWest1,
            "us-west-2" => Amazon.RegionEndpoint.USWest2,
            "eu-west-1" => Amazon.RegionEndpoint.EUWest1,
            "eu-west-2" => Amazon.RegionEndpoint.EUWest2,
            "eu-central-1" => Amazon.RegionEndpoint.EUCentral1,
            "ap-south-1" => Amazon.RegionEndpoint.APSouth1,
            "ap-northeast-1" => Amazon.RegionEndpoint.APNortheast1,
            "ap-northeast-2" => Amazon.RegionEndpoint.APNortheast2,
            "ap-southeast-1" => Amazon.RegionEndpoint.APSoutheast1,
            "ap-southeast-2" => Amazon.RegionEndpoint.APSoutheast2,
            _ => Amazon.RegionEndpoint.USEast1
        };

        private List<dynamic> ProcessTables(IList<Block> blocks)
        {
            var allTables = new List<dynamic>();
            var tableBlocks = blocks.Where(b => b.BlockType == BlockType.TABLE).ToList();

            int tableIndex = 0;
            foreach (var tableBlock in tableBlocks)
            {
                var cellBlocks = blocks.Where(b =>
                    b.BlockType == BlockType.CELL &&
                    b.Relationships?.Any(r => r.Type == RelationshipType.CHILD) == true
                ).ToList();

                var tableData = new
                {
                    TableIndex = tableIndex++,
                    RowCount = cellBlocks.Any() ? cellBlocks.Max(c => c.RowIndex ?? 0) + 1 : 0,
                    ColumnCount = cellBlocks.Any() ? cellBlocks.Max(c => c.ColumnIndex ?? 0) + 1 : 0,
                    Cells = cellBlocks.Select(cell => new
                    {
                        Row = cell.RowIndex ?? 0,
                        Col = cell.ColumnIndex ?? 0,
                        Content = GetTextForBlock(cell.Id, blocks),
                        Kind = "table-cell",
                        SpanCount = (cell.RowSpan ?? 1) * (cell.ColumnSpan ?? 1)
                    }).ToList()
                };
                allTables.Add(tableData);
            }

            return allTables;
        }

        private List<dynamic> ProcessKeyValuePairs(IList<Block> blocks)
        {
            var allKvps = new List<dynamic>();

            var keyBlocks = blocks.Where(b =>
                b.BlockType == BlockType.KEY_VALUE_SET &&
                b.EntityTypes?.Contains("KEY") == true).ToList();

            foreach (var keyBlock in keyBlocks)
            {
                string keyText = GetTextForBlock(keyBlock.Id, blocks);

                var valueRelationship = keyBlock.Relationships?.FirstOrDefault(r => r.Type == RelationshipType.VALUE);
                string valueText = "";
                double valueConfidence = 0.0;

                if (valueRelationship?.Ids?.Any() == true)
                {
                    var valueBlockId = valueRelationship.Ids.First();
                    var valueBlock = blocks.FirstOrDefault(b => b.Id == valueBlockId);
                    if (valueBlock != null)
                    {
                        valueText = GetTextForBlock(valueBlock.Id, blocks);
                        valueConfidence = ConvertConfidence(valueBlock.Confidence);
                    }
                }

                allKvps.Add(new
                {
                    Key = new
                    {
                        Text = keyText,
                        Confidence = ConvertConfidence(keyBlock.Confidence),
                        Regions = new[]
                        {
                            new
                            {
                                Page = 1,
                                Polygon = GetPolygonCoords(keyBlock.Geometry)
                            }
                        }
                    },
                    Value = new
                    {
                        Text = valueText,
                        Confidence = valueConfidence,
                        Regions = new[]
                        {
                            new
                            {
                                Page = 1,
                                Polygon = Array.Empty<double>()
                            }
                        }
                    },
                    Confidence = ConvertConfidence(keyBlock.Confidence)
                });
            }

            return allKvps;
        }

        private List<dynamic> ProcessOcrData(IList<Block> blocks)
        {
            var pageData = new
            {
                PageNumber = 1,
                Angle = 0.0,
                Dimensions = new { Width = 0.0, Height = 0.0, Unit = "pixels" },
                Lines = blocks
                    .Where(b => b.BlockType == BlockType.LINE)
                    .Select(line => new
                    {
                        Text = line.Text ?? "",
                        Polygon = GetPolygonCoords(line.Geometry)
                    }).ToArray(),
                Words = blocks
                    .Where(b => b.BlockType == BlockType.WORD)
                    .Select(word => new
                    {
                        Text = word.Text ?? "",
                        Confidence = ConvertConfidence(word.Confidence),  // ✅ FIXED: Extract to variable first
                        Polygon = GetPolygonCoords(word.Geometry)
                    }).ToArray()
            };

            return new List<dynamic> { pageData };
        }

        // ✅ HELPER: Safe conversion method
        private double ConvertConfidence(float? confidence) => confidence.HasValue ? (double)confidence.Value : 0.0;

        private double[] GetPolygonCoords(Geometry geometry)
        {
            if (geometry?.Polygon == null || !geometry.Polygon.Any())
                return Array.Empty<double>();

            // Safely handle nullable X and Y, defaulting to 0.0 if null
            return geometry.Polygon
                .SelectMany(p => new double[] { p.X.HasValue ? (double)p.X.Value : 0.0, p.Y.HasValue ? (double)p.Y.Value : 0.0 })
                .ToArray();
        }

        private string GetTextForBlock(string? blockId, IList<Block> blocks)
        {
            if (string.IsNullOrEmpty(blockId))
                return "";

            var block = blocks.FirstOrDefault(b => b.Id == blockId);
            if (block != null)
            {
                if (!string.IsNullOrEmpty(block.Text))
                    return block.Text;

                var childIds = block.Relationships?.FirstOrDefault(r => r.Type == RelationshipType.CHILD)?.Ids;
                if (childIds != null && childIds.Any())
                {
                    var childTexts = childIds.SelectMany(id =>
                        blocks.Where(b => b.Id == id).Select(b => b.Text ?? "")).ToList();
                    return string.Join(" ", childTexts.Where(s => !string.IsNullOrEmpty(s)));
                }
            }

            return "";
        }
    }
}