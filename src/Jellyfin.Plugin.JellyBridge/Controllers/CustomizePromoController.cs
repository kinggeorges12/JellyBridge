using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class CustomizePromoController : ControllerBase
    {
        private readonly DebugLogger<CustomizePromoController> _logger;
        private readonly Services.PlaceholderVideoGenerator _placeholderVideoGenerator;

        public CustomizePromoController(ILoggerFactory loggerFactory, Services.PlaceholderVideoGenerator placeholderVideoGenerator)
        {
            _logger = new DebugLogger<CustomizePromoController>(loggerFactory.CreateLogger<CustomizePromoController>());
            _placeholderVideoGenerator = placeholderVideoGenerator;
        }

        /// <summary>
        /// Generate custom promo videos for the library.
        /// </summary>
        [HttpPost("PromoVideos")]
        public async Task<IActionResult> Generate()
        {
            _logger.LogDebug("Custom promo videos generation requested");

            try
            {
                // Perform the actual video generation logic here
                var numRefreshed = await _placeholderVideoGenerator.RefreshAllPlaceholdersAsync();

                return Ok(new
                {
                    success = true,
                    message = "Custom promo videos generated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom promo videos generation failed");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Generation failed: {ex.Message}",
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get status of custom promo images for both types.
        /// </summary>
        [HttpGet("PromoVideos")]
        public async Task<IActionResult> Status([FromQuery] string? movie = null, [FromQuery] string? show = null)
        {
            _logger.LogDebug("Custom placeholder status requested");

            try
            {
                var config = Plugin.GetConfiguration();
                var customMovie = movie ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.CustomMoviePromo));
                var customShow = show ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.CustomShowPromo));
                config.CustomMoviePromo = customMovie;
                config.CustomShowPromo = customShow;

                return Ok(new
                {
                    success = true,
                    movie = GetAssetStatus(customMovie),
                    show = GetAssetStatus(customShow)
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
        private object GetAssetStatus(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return new { hasCustom = false, fileName = (string?)null, fileSize = (long?)null };
            }
            if (System.IO.File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                return new { hasCustom = true, fileName = fileInfo.Name, fileSize = (long?)fileInfo.Length };
            }
            else
            {
                // Config says there's a custom asset but file is missing — clean up
                _logger.LogDebug("Custom asset file missing, config will be stale until next save: {filePath}", filePath);
            }

            return new { hasCustom = false, fileName = (string?)null, fileSize = (long?)null };
        }

    }
}
