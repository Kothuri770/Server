using ImageMagick;
using PDFtoImage;
using SkiaSharp;
using System.IO;
using System.Threading.Tasks;

namespace Server.Services
{
    public class ImageToPdfService : IImageToPdfService
    {
        private readonly ILogger<ImageToPdfService> _logger;

        public ImageToPdfService(ILogger<ImageToPdfService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConvertImagesToPdfAsync(IEnumerable<string> imagePaths, string pdfPath)
        {
            try
            {
                // Ensure the directory for the PDF exists
                var directory = Path.GetDirectoryName(pdfPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var images = new MagickImageCollection())
                {
                    foreach (var imagePath in imagePaths)
                    {
                        if (!File.Exists(imagePath))
                        {
                            _logger.LogWarning($"Image file not found: {imagePath}");
                            continue;
                        }

                        // Read all pages/frames (handles both single images and multi-page TIFFs/PDFs)
                        try
                        {
                            var extension = Path.GetExtension(imagePath).ToLowerInvariant();
                            if (extension == ".pdf")
                            {
                                try
                                {
                                    var fullPath = Path.GetFullPath(imagePath);
                                    long fileSize = new FileInfo(fullPath).Length;
                                    _logger.LogInformation($"[PDF2PDF] Starting PDF rendering. Path: {fullPath}, Size: {fileSize} bytes");
                                    
                                    var renderOptions = new RenderOptions { Dpi = 200 }; // Reduced from 300 to 200 for better size balance
                                    int pageCount = 0;

                                    using (var fs = File.OpenRead(fullPath))
                                    {
                                        _logger.LogInformation("[PDF2PDF] File stream opened successfully.");
                                        foreach (var bitmap in Conversion.ToImages(fs, options: renderOptions))
                                        {
                                            pageCount++;
                                            _logger.LogInformation($"[PDF2PDF] Rendering page {pageCount}...");
                                            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
                                            {
                                                var bytes = data.ToArray();
                                                images.Add(new MagickImage(bytes));
                                                _logger.LogInformation($"[PDF2PDF] Added page {pageCount} to collection ({bytes.Length} bytes).");
                                            }
                                            bitmap.Dispose();
                                        }
                                    }
                                    _logger.LogInformation($"[PDF2PDF] Completed rendering. Total pages: {pageCount}");
                                }
                                catch (Exception pdfEx)
                                {
                                    _logger.LogError(pdfEx, $"[PDF2PDF] CRITICAL ERROR rendering PDF {imagePath}: {pdfEx.Message}");
                                    throw;
                                }
                            }
                            else
                            {
                                // Direct image reading
                                images.Read(imagePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to read file for PDF conversion: {imagePath}");
                        }
                    }

                    if (images.Count == 0) return false;

                    // Set output format to PDF
                    foreach (var image in images)
                    {
                        try
                        {
                            image.Format = MagickFormat.Pdf;
                            
                            // Optimize for size
                            if (image.ColorType == ColorType.Bilevel || image.ColorType == ColorType.Grayscale)
                            {
                                image.Quantize(new QuantizeSettings { Colors = 2, ColorSpace = ColorSpace.Gray });
                                image.Settings.Compression = CompressionMethod.Fax; // CCITT Group 4 equivalent for PDF
                                image.Depth = 1;
                            }
                            else
                            {
                                image.Settings.Compression = CompressionMethod.Zip; // Flate compression for color/grayscale
                            }
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogError(imgEx, $"[PDF2PDF] Error setting compression for an image: {imgEx.Message}");
                            image.Settings.Compression = CompressionMethod.Zip;
                        }
                    }

                    // Write to output path
                    await Task.Run(() => images.Write(pdfPath));
                }

                return File.Exists(pdfPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting documents to PDF: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConvertImageToPdfAsync(string imagePath, string pdfPath)
        {
            return await ConvertImagesToPdfAsync(new[] { imagePath }, pdfPath);
        }
    }
}
