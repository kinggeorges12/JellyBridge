using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class OrganizeDiscoverLibraryController : ControllerBase
    {
        private readonly DebugLogger<OrganizeDiscoverLibraryController> _logger;
        private readonly MetadataService _metadataService;

        public OrganizeDiscoverLibraryController(ILoggerFactory loggerFactory, MetadataService metadataService)
        {
            _logger = new DebugLogger<OrganizeDiscoverLibraryController>(loggerFactory.CreateLogger<OrganizeDiscoverLibraryController>());
            _metadataService = metadataService;
        }

        /// <summary>
        /// Generate network service folders in the JellyBridge library directory.
        /// </summary>
        [HttpPost("GenerateLibraryFolders")]
        public async Task<IActionResult> GenerateLibraryFolders()
        {
            _logger.LogInformation("Generate Library Folders requested from plugin configuration page.");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    var config = Plugin.GetConfiguration();
                    var networkMap = config.NetworkMap ?? new List<JellyseerrNetwork>();
                    
                    if (networkMap.Count == 0)
                    {
                        throw new InvalidOperationException("No networks configured. Please select networks in the Import Discover Content section first.");
                    }
                    
                    // Use MetadataService to create network folders
                    (List<string> movieFolders, List<string> showFolders, List<string> mixedFolders) = await _metadataService.CreateEmptyLibraryFoldersAsync();
                    List<string> allFolders = [.. movieFolders, .. showFolders, .. mixedFolders];

                    var message = $"Successfully processed folder creation. {movieFolders.Count} movie folder(s) created, {showFolders.Count} show folder(s) created, {mixedFolders.Count} mixed folder(s) created.";
                    
                    await Task.CompletedTask; // Satisfy async requirement for consistency
                    return new
                    {
                        success = true,
                        message = message,
                        movieFolders = movieFolders,
                        showFolders = showFolders,
                        mixedFolders = mixedFolders,
                        totalNetworks = networkMap.Count
                    };
                }, _logger, "Generate Library Folders");
                
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Generate Library Folders timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Network folder generation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating network folders");
                return StatusCode(500, new { 
                    success = false,
                    error = "Failed to generate library folders",
                    message = ex.Message,
                    details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}"
                });
            }
        }
    }
}

