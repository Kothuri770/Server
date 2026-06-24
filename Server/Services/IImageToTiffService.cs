using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.Services
{
    public interface IImageToTiffService
    {
        /// <summary>
        /// Converts multiple image files into a single multi-page TIFF.
        /// </summary>
        /// <param name="imagePaths">List of paths to source images</param>
        /// <param name="tiffPath">Path where the TIFF should be saved</param>
        /// <returns>True if successful</returns>
        Task<bool> ConvertImagesToTiffAsync(IEnumerable<string> imagePaths, string tiffPath);

        /// <summary>
        /// Converts a single image to a TIFF.
        /// </summary>
        /// <param name="imagePath">Source image path</param>
        /// <param name="tiffPath">Target TIFF path</param>
        /// <returns>True if successful</returns>
        Task<bool> ConvertImageToTiffAsync(string imagePath, string tiffPath);
    }
}
