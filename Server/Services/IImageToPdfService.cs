namespace Server.Services
{
    public interface IImageToPdfService
    {
        /// <summary>
        /// Converts an image file to a single-page PDF.
        /// </summary>
        /// <param name="imagePath">Path to the source image (BMP, PNG, TIFF, etc.)</param>
        /// <param name="pdfPath">Path where the PDF should be saved</param>
        /// <returns>True if successful</returns>
        Task<bool> ConvertImageToPdfAsync(string imagePath, string pdfPath);

        /// <summary>
        /// Converts multiple image files into a single multi-page PDF.
        /// </summary>
        /// <param name="imagePaths">List of paths to source images</param>
        /// <param name="pdfPath">Path where the PDF should be saved</param>
        /// <returns>True if successful</returns>
        Task<bool> ConvertImagesToPdfAsync(IEnumerable<string> imagePaths, string pdfPath);
    }
}
