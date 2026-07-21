using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class CustomizePromoController : ControllerBase
    {
        private readonly DebugLogger<CustomizePromoController> _logger;
        private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;

        public CustomizePromoController(ILoggerFactory loggerFactory, PlaceholderVideoGenerator placeholderVideoGenerator)
        {
            _logger = new DebugLogger<CustomizePromoController>(loggerFactory.CreateLogger<CustomizePromoController>());
            _placeholderVideoGenerator = placeholderVideoGenerator;
        }

        /// <summary>
        ///     Generate custom promo videos for movies and series.
        /// </summary>
        /// <returns>A JSON response indicating the success or failure of the operation.</returns>
        [HttpPost("PromoVideos")]
        public async Task<IActionResult> Generate()
        {
            _logger.LogDebug("Custom promo videos generation requested");
            try
            {
                var (successfulItems, failedItems) = await _placeholderVideoGenerator.RefreshAllPlaceholdersAsync();
                
                // Build the result message
                string resultMessage = $"Custom promo videos generated successfully for {successfulItems.Count} items.";
                if (failedItems.Count > 0)
                {
                    resultMessage += $"\nFailed for {failedItems.Count} items:\n" + string.Join("\n", failedItems);
                }
                
                return Ok(new
                {
                    success = true,
                    result = resultMessage,
                    generated = successfulItems.Count,
                    failed = failedItems.Count,
                    failedItems = failedItems.Count > 0 ? failedItems : null
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
        ///     Download the generated promo video for a specific type (movies or series).
        /// </summary>
        /// <param name="type">The type of promo video to download (movies or series).</param>
        /// <returns>The promo video file.</returns>
        [HttpGet("PromoVideos/{type}")]
        public async Task<IActionResult> DownloadPromoVideo(string type)
        {
            _logger.LogDebug($"{type} promo video requested");
            
            try
            {
                var filePath = string.Empty;
                
                if (string.Equals(type, "movies", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = await _placeholderVideoGenerator.GetMoviesPlaceholderAsync();
                }
                else if (string.Equals(type, "series", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = await _placeholderVideoGenerator.GetSeriesPlaceholderAsync();
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid type. Must be 'movies' or 'series'."
                    });
                }

                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"No promo video found for type: {type}"
                    });
                }

                return PhysicalFile(
                    filePath,
                    "video/mp4",
                    enableRangeProcessing: true
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get {Type} promo video", type);
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }
}
