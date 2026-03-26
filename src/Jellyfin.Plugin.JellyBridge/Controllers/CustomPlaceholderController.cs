using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class CustomPlaceholderController : ControllerBase
    {
        private readonly DebugLogger<CustomPlaceholderController> _logger;

        private static readonly string CustomAssetsFolder = "custom-assets";
        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

        public CustomPlaceholderController(ILoggerFactory loggerFactory)
        {
            _logger = new DebugLogger<CustomPlaceholderController>(loggerFactory.CreateLogger<CustomPlaceholderController>());
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
        public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string type)
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

                // Invalidate cached placeholders
                InvalidateCache(type);

                _logger.LogInformation("Custom placeholder uploaded successfully: {FileName} for type {Type}", targetFileName, type);

                return Ok(new
                {
                    success = true,
                    message = $"Custom {type} placeholder uploaded successfully.",
                    details = new { fileName = targetFileName, size = file.Length }
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
        public IActionResult Delete([FromQuery] string type)
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

                // Invalidate cached placeholders
                InvalidateCache(type);

                _logger.LogInformation("Custom placeholder deleted successfully for type {Type}", type);

                return Ok(new
                {
                    success = true,
                    message = $"Custom {type} placeholder removed. Default placeholder will be used."
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
            if (File.Exists(filePath))
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
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogDebug("Deleted existing custom asset: {FilePath}", filePath);
                }
            }
        }

        /// <summary>
        /// Invalidates cached placeholder videos for the given type.
        /// Deletes matching cached files so they regenerate on next sync.
        /// </summary>
        private void InvalidateCache(string type)
        {
            try
            {
                var jellyBridgeTempDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.JellyBridgeTempDirectory));
                var placeholderPath = Path.Combine(jellyBridgeTempDirectory, "placeholders");

                if (!Directory.Exists(placeholderPath))
                {
                    return;
                }

                // Movie type: invalidate movie_*.mp4
                // Show type: invalidate S00E9999_*.mp4 (season asset used for shows)
                var patterns = type == "movie"
                    ? new[] { "movie_*" }
                    : new[] { "S00E9999_*" };

                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(placeholderPath, pattern + ".mp4");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                        _logger.LogDebug("Invalidated cached placeholder: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to invalidate placeholder cache for type {Type}", type);
            }
        }
    }
}
