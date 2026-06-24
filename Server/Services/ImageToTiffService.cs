using ImageMagick;
using PDFtoImage;
using SkiaSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Server.Services
{
    public class ImageToTiffService : IImageToTiffService
    {
        private readonly ILogger<ImageToTiffService> _logger;

        public ImageToTiffService(ILogger<ImageToTiffService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConvertImagesToTiffAsync(IEnumerable<string> imagePaths, string tiffPath)
        {
            try
            {
                var paths = imagePaths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).ToList();
                if (!paths.Any()) return false;

                // Ensure directory exists
                var directory = Path.GetDirectoryName(tiffPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var images = new MagickImageCollection())
                {
                    foreach (var path in paths)
                    {
                        try
                        {
                            var extension = Path.GetExtension(path).ToLowerInvariant();
                            if (extension == ".pdf")
                            {
                                try
                                {
                                    var fullPath = Path.GetFullPath(path);
                                    long fileSize = new FileInfo(fullPath).Length;
                                    _logger.LogInformation($"[PDF2TIFF] Starting PDF rendering. Path: {fullPath}, Size: {fileSize} bytes");
                                    
                                    var renderOptions = new RenderOptions { Dpi = 200 }; // Reduced from 300 to 200 for better size balance
                                    int pageCount = 0;

                                    using (var fs = File.OpenRead(fullPath))
                                    {
                                        _logger.LogInformation("[PDF2TIFF] File stream opened successfully.");
                                        foreach (var bitmap in Conversion.ToImages(fs, options: renderOptions))
                                        {
                                            pageCount++;
                                            _logger.LogInformation($"[PDF2TIFF] Rendering page {pageCount}...");
                                            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
                                            {
                                                var bytes = data.ToArray();
                                                images.Add(new MagickImage(bytes));
                                                _logger.LogInformation($"[PDF2TIFF] Added page {pageCount} to collection ({bytes.Length} bytes).");
                                            }
                                            bitmap.Dispose();
                                        }
                                    }
                                    _logger.LogInformation($"[PDF2TIFF] Completed rendering. Total pages: {pageCount}");
                                }
                                catch (Exception pdfEx)
                                {
                                    _logger.LogError(pdfEx, $"[PDF2TIFF] CRITICAL ERROR rendering PDF {path}: {pdfEx.Message}");
                                    throw;
                                }
                            }
                            else
                            {
                                // Direct image reading
                                _logger.LogInformation($"[PDF2TIFF] Reading image directly: {path}");
                                images.Read(path);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to read file for TIFF conversion: {path}");
                        }
                    }

                    if (images.Count == 0)
                    {
                        _logger.LogWarning("[PDF2TIFF] No images collected for conversion.");
                        return false;
                    }

                    _logger.LogInformation($"[PDF2TIFF] Processing {images.Count} images for TIFF output...");

                    // Set format to TIFF for all images in collection
                    foreach (var image in images)
                    {
                        try
                        {
                            image.Format = MagickFormat.Tif;
                            
                            // Optimize for size: Use Group4 for monochrome (1-bit) or LZW for others
                            // Group4 REQUIREMENT: 1-bit (bilevel)
                            if (image.ColorType == ColorType.Bilevel || image.ColorType == ColorType.Grayscale)
                            {
                                _logger.LogInformation($"[PDF2TIFF] Applying Group4 compression for page with color type: {image.ColorType}");
                                image.Quantize(new QuantizeSettings { Colors = 2, ColorSpace = ColorSpace.Gray });
                                image.Settings.Compression = CompressionMethod.Group4;
                                image.Depth = 1;
                            }
                            else
                            {
                                _logger.LogInformation($"[PDF2TIFF] Applying LZW compression for page with color type: {image.ColorType}");
                                image.Settings.Compression = CompressionMethod.LZW;
                            }
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogError(imgEx, $"[PDF2TIFF] Error setting compression for an image: {imgEx.Message}. Falling back to default LZW.");
                            image.Settings.Compression = CompressionMethod.LZW;
                        }
                    }

                    // Write as multi-page TIFF
                    _logger.LogInformation($"[PDF2TIFF] Writing multi-page TIFF to: {tiffPath}");
                    await Task.Run(() => images.Write(tiffPath));
                    _logger.LogInformation($"[PDF2TIFF] Finished writing file. Exists: {File.Exists(tiffPath)}");
                }

                return File.Exists(tiffPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting documents to TIFF: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConvertImageToTiffAsync(string imagePath, string tiffPath)
        {
            return await ConvertImagesToTiffAsync(new[] { imagePath }, tiffPath);
        }
    }
}
