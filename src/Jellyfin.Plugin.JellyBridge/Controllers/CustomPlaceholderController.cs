using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class CustomPlaceholderController : ControllerBase
    {
        private readonly DebugLogger<CustomPlaceholderController> _logger;
        private readonly Services.PlaceholderVideoGenerator _placeholderVideoGenerator;

        private static readonly string CustomAssetsFolder = "custom-assets";
        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

        public CustomPlaceholderController(ILoggerFactory loggerFactory, Services.PlaceholderVideoGenerator placeholderVideoGenerator)
        {
            _logger = new DebugLogger<CustomPlaceholderController>(loggerFactory.CreateLogger<CustomPlaceholderController>());
            _placeholderVideoGenerator = placeholderVideoGenerator;
        }

        /// <summary>
        /// Gets the custom assets directory path inside the plugin data folder.
        /// </summary>
        private string GetCustomAssetsDirectory()
        {
            var dataFolderPath = Plugin.Instance.DataFolderPath;
            return Path.Combine(dataFolderPath, CustomAssetsFolder);
        }

        /// <summary>
        /// Upload a custom placeholder image (PNG/JPG) for movies or shows.
        /// </summary>
        [HttpPost("CustomPlaceholder/Upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromQuery] string type)
        {
            _logger.LogInformation("Custom placeholder upload requested for type: {Type}", type);

            try
            {
                // Validate type parameter
                if (string.IsNullOrEmpty(type) || (type != "movie" && type != "show"))
                {
                    return BadRequest(new { success = false, message = "Invalid type parameter. Must be 'movie' or 'show'." });
                }

                // Validate file
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { success = false, message = "No file provided or file is empty." });
                }

                if (file.Length > MaxFileSizeBytes)
                {
                    return BadRequest(new { success = false, message = $"File exceeds maximum size of {MaxFileSizeBytes / (1024 * 1024)}MB." });
                }

                // Validate extension
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!AllowedExtensions.Contains(extension))
                {
                    return BadRequest(new { success = false, message = "Invalid file type. Only PNG and JPG files are allowed." });
                }

                // Ensure custom assets directory exists
                var customAssetsDir = GetCustomAssetsDirectory();
                Directory.CreateDirectory(customAssetsDir);

                // Build target filename: custom_movie.{ext} or custom_show.{ext}
                var targetFileName = $"custom_{type}{extension}";
                var targetPath = Path.Combine(customAssetsDir, targetFileName);

                // Delete any existing custom asset for this type (may have different extension)
                DeleteExistingCustomAsset(customAssetsDir, type);

                // Save the uploaded file
                using (var stream = new FileStream(targetPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Update config
                var config = Plugin.GetConfiguration();
                if (type == "movie")
                {
                    config.CustomMoviePlaceholderFileName = targetFileName;
                }
                else
                {
                    config.CustomShowPlaceholderFileName = targetFileName;
                }
                Plugin.Instance.UpdateConfiguration(config);

                // Invalidate cache and refresh all existing placeholders in the library
                var refreshed = await _placeholderVideoGenerator.RefreshAllPlaceholdersAsync(type);

                _logger.LogInformation("Custom placeholder uploaded successfully: {FileName} for type {Type}, refreshed {Refreshed} existing items", targetFileName, type, refreshed);

                return Ok(new
                {
                    success = true,
                    message = $"Custom {type} placeholder uploaded successfully. {refreshed} existing item(s) updated.",
                    details = new { fileName = targetFileName, size = file.Length, refreshedCount = refreshed }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom placeholder upload failed for type {Type}", type);
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Upload failed: {ex.Message}",
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Delete a custom placeholder image for movies or shows.
        /// </summary>
        [HttpDelete("CustomPlaceholder/Delete")]
        public async Task<IActionResult> Delete([FromQuery] string type)
        {
            _logger.LogInformation("Custom placeholder delete requested for type: {Type}", type);

            try
            {
                // Validate type parameter
                if (string.IsNullOrEmpty(type) || (type != "movie" && type != "show"))
                {
                    return BadRequest(new { success = false, message = "Invalid type parameter. Must be 'movie' or 'show'." });
                }

                var customAssetsDir = GetCustomAssetsDirectory();

                // Delete the custom asset file
                DeleteExistingCustomAsset(customAssetsDir, type);

                // Clear config
                var config = Plugin.GetConfiguration();
                if (type == "movie")
                {
                    config.CustomMoviePlaceholderFileName = string.Empty;
                }
                else
                {
                    config.CustomShowPlaceholderFileName = string.Empty;
                }
                Plugin.Instance.UpdateConfiguration(config);

                // Invalidate cache and refresh all existing placeholders back to default
                var refreshed = await _placeholderVideoGenerator.RefreshAllPlaceholdersAsync(type);

                _logger.LogInformation("Custom placeholder deleted successfully for type {Type}, refreshed {Refreshed} existing items back to default", type, refreshed);

                return Ok(new
                {
                    success = true,
                    message = $"Custom {type} placeholder removed. {refreshed} existing item(s) reverted to default."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom placeholder delete failed for type {Type}", type);
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Delete failed: {ex.Message}",
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get status of custom placeholder images for both types.
        /// </summary>
        [HttpGet("CustomPlaceholder/Status")]
        public IActionResult Status()
        {
            _logger.LogDebug("Custom placeholder status requested");

            try
            {
                var config = Plugin.GetConfiguration();
                var customAssetsDir = GetCustomAssetsDirectory();

                return Ok(new
                {
                    success = true,
                    movie = GetAssetStatus(customAssetsDir, config.CustomMoviePlaceholderFileName),
                    show = GetAssetStatus(customAssetsDir, config.CustomShowPlaceholderFileName)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom placeholder status check failed");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Status check failed: {ex.Message}",
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Gets the status of a custom asset file.
        /// </summary>
        private object GetAssetStatus(string customAssetsDir, string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return new { hasCustom = false, fileName = (string?)null, fileSize = (long?)null };
            }

            var filePath = Path.Combine(customAssetsDir, fileName);
            if (System.IO.File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                return new { hasCustom = true, fileName = fileName, fileSize = (long?)fileInfo.Length };
            }

            // Config says there's a custom asset but file is missing — clean up
            _logger.LogDebug("Custom asset file missing, config will be stale until next save: {FileName}", fileName);
            return new { hasCustom = false, fileName = (string?)null, fileSize = (long?)null };
        }

        /// <summary>
        /// Deletes any existing custom asset for the given type (handles different extensions).
        /// </summary>
        private void DeleteExistingCustomAsset(string customAssetsDir, string type)
        {
            if (!Directory.Exists(customAssetsDir))
            {
                return;
            }

            var prefix = $"custom_{type}";
            foreach (var ext in AllowedExtensions)
            {
                var filePath = Path.Combine(customAssetsDir, prefix + ext);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    _logger.LogDebug("Deleted existing custom asset: {FilePath}", filePath);
                }
            }
        }

    }
}
