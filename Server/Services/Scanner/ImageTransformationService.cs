using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Server.Services.Configuration;
using Tesseract;
using System.IO;
using System;

namespace Server.Services.Scanner
{
    public interface IImageTransformationService
    {
        Task<TransformResult> RotateImageAsync(string fileName, long batchId, int degrees);
        Task<string> GenerateThumbnailAsync(string filePath, int width = 150, int height = 150);
        Task AutoOrientBatchImagesAsync(int batchId);
    }

    public class ImageTransformationService : IImageTransformationService
    {
        private readonly ILogger<ImageTransformationService> _logger;
        private readonly IConfigurationService _configService;
        private readonly IFileStorageService _fileStorageService;

        public ImageTransformationService(
            IConfigurationService configService,
            ILogger<ImageTransformationService> logger,
            IFileStorageService fileStorageService)
        {
            _logger = logger;
            _configService = configService;
            _fileStorageService = fileStorageService;
        }

        public async Task<TransformResult> RotateImageAsync(string fileName, long batchId, int degrees)
        {
            try
            {
                var uploadPath = await _fileStorageService.GetBatchPathAsync((int)batchId);
                var filePath = Path.Combine(uploadPath, fileName);

                if (!System.IO.File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext == ".tif" || ext == ".tiff")
                {
                    using (var collection = new ImageMagick.MagickImageCollection(filePath))
                    {
                        foreach (var frame in collection)
                        {
                            frame.Rotate(degrees);
                        }
                        await collection.WriteAsync(filePath);
                    }
                }
                else
                {
                    // Overwrite the original image
                    using (var image = await Image.LoadAsync<Rgba32>(filePath))
                    {
                        image.Mutate(x => x.Rotate(degrees));
                        await image.SaveAsync(filePath);
                    }
                }

                // Delete the old thumbnail so GenerateThumbnailAsync forces a new one
                var thumbFileName = $"thumb_{Path.GetFileName(filePath)}";
                var thumbPath = Path.Combine(Path.GetDirectoryName(filePath), thumbFileName);
                if (System.IO.File.Exists(thumbPath))
                {
                    System.IO.File.Delete(thumbPath);
                }

                // Generate new thumbnail
                var newThumbPath = await GenerateThumbnailAsync(filePath, 150, 150);

                return new TransformResult
                {
                    NewUrl = $"/api/image/batch/{batchId}/file/{Uri.EscapeDataString(fileName)}",
                    ThumbnailUrl = $"/api/image/batch/{batchId}/file/{Uri.EscapeDataString(Path.GetFileName(newThumbPath))}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rotating image {FileName} for batch {BatchId}", fileName, batchId);
                throw;
            }
        }

        public async Task AutoOrientBatchImagesAsync(int batchId)
        {
            try
            {
                var uploadPath = await _fileStorageService.GetBatchPathAsync((int)batchId);
                if (!Directory.Exists(uploadPath))
                {
                    _logger.LogWarning("Batch directory not found for batch {BatchId}", batchId);
                    return;
                }

                string tesseractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessract");
                using var engine = new TesseractEngine(tesseractPath, "osd", EngineMode.TesseractOnly);

                var files = Directory.GetFiles(uploadPath);
                foreach (var filePath in files)
                {
                    try
                    {
                        var ext = Path.GetExtension(filePath).ToLowerInvariant();
                        if (ext == ".tif" || ext == ".tiff" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif")
                        {
                            try
                            {
                                if (ext != ".tif" && ext != ".tiff")
                                {
                                    using var image = new ImageMagick.MagickImage(filePath);
                                    if (image.Orientation != ImageMagick.OrientationType.Undefined && image.Orientation != ImageMagick.OrientationType.TopLeft)
                                    {
                                        image.AutoOrient();
                                        await image.WriteAsync(filePath);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to AutoOrient EXIF for {FilePath}", filePath);
                            }

                            using var pix = Pix.LoadFromFile(filePath);
                            int orientation = 0;
                            float confidence = 0f;

                            try
                            {
                                using var page = engine.Process(pix, PageSegMode.OsdOnly);
                                page.DetectBestOrientation(out orientation, out confidence);
                            }
                            catch (Exception)
                            {
                                // Fallback for sparse documents (like invoices or sideways scans)
                                engine.SetVariable("min_characters_to_try", "5");
                                try
                                {
                                    using var fallbackPage = engine.Process(pix, PageSegMode.OsdOnly);
                                    fallbackPage.DetectBestOrientation(out orientation, out confidence);
                                }
                                catch
                                {
                                    orientation = 0;
                                }
                                engine.SetVariable("min_characters_to_try", "50"); // Reset for next image
                            }

                            int degrees = 0;
                            float requiredConfidence = (ext == ".tif" || ext == ".tiff") ? 0.5f : 0.3f;

                            if (confidence >= requiredConfidence)
                            {
                                if (orientation == 180) degrees = 180;
                                else if (orientation == 90) degrees = 270; 
                                else if (orientation == 270) degrees = 90;
                            }
                            else
                            {
                                _logger.LogInformation("Ignoring Tesseract orientation {Orientation} due to low confidence {Confidence}", orientation, confidence);
                            }

                            if (degrees != 0)
                            {
                                if (ext == ".tif" || ext == ".tiff")
                                {
                                    using var collection = new ImageMagick.MagickImageCollection(filePath);
                                    foreach (var frame in collection)
                                    {
                                        frame.Rotate(degrees);
                                    }
                                    await collection.WriteAsync(filePath);
                                }
                                else
                                {
                                    using var image = new ImageMagick.MagickImage(filePath);
                                    image.Rotate(degrees);
                                    await image.WriteAsync(filePath);
                                }

                                // Delete old thumbnail if it exists so it gets regenerated upright
                                var thumbFileName = $"thumb_{Path.GetFileName(filePath)}";
                                var thumbPath = Path.Combine(Path.GetDirectoryName(filePath), thumbFileName);
                                if (System.IO.File.Exists(thumbPath))
                                {
                                    System.IO.File.Delete(thumbPath);
                                }

                                _logger.LogInformation("Auto-rotated image {FileName} by {Degrees} degrees. Confidence: {Confidence}", Path.GetFileName(filePath), degrees, confidence);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing OSD/auto-rotation for file {FilePath}", filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoOrientBatchImagesAsync for batch {BatchId}", batchId);
            }
        }

        public async Task<string> GenerateThumbnailAsync(string filePath, int width = 150, int height = 150)
        {
            try
            {
                var thumbFileName = $"thumb_{Path.GetFileName(filePath)}";
                var thumbPath = Path.Combine(Path.GetDirectoryName(filePath), thumbFileName);

                _logger.LogInformation("Generating thumbnail: {ThumbPath}", thumbPath);

                // Don't regenerate if already exists
                if (System.IO.File.Exists(thumbPath))
                {
                    _logger.LogDebug("Thumbnail already exists: {ThumbPath}", thumbPath);
                    return thumbPath;
                }

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext == ".tif" || ext == ".tiff")
                {
                    var readSettings = new ImageMagick.MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
                    using (var image = new ImageMagick.MagickImage(filePath, readSettings))
                    {
                        var size = new ImageMagick.MagickGeometry((uint)width, (uint)height)
                        {
                            IgnoreAspectRatio = false
                        };
                        image.Resize(size);
                        await image.WriteAsync(thumbPath);
                    }
                }
                else
                {
                    // Generate thumbnail
                    using (var image = await Image.LoadAsync<Rgba32>(filePath))
                    {
                        var options = new ResizeOptions
                        {
                            Mode = ResizeMode.Max,
                            Size = new Size(width, height)
                        };

                        image.Mutate(x => x.Resize(options));
                        await image.SaveAsync(thumbPath);
                    }
                }

                _logger.LogInformation("Thumbnail generated successfully: {ThumbPath}", thumbPath);
                return thumbPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating thumbnail for {FilePath}", filePath);
                // Return original path as fallback
                return filePath;
            }
        }
    }

    public class TransformResult
    {
        public string NewUrl { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}
