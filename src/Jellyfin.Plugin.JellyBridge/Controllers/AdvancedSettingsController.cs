using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class AdvancedSettingsController : ControllerBase
    {
        private readonly DebugLogger<AdvancedSettingsController> _logger;
        private readonly LibraryService _libraryService;

        public AdvancedSettingsController(ILoggerFactory loggerFactory, LibraryService libraryService)
        {
            _logger = new DebugLogger<AdvancedSettingsController>(loggerFactory.CreateLogger<AdvancedSettingsController>());
            _libraryService = libraryService;
        }

        /// <summary>
        /// Recycle all Jellyseerr library data.
        /// </summary>
        [HttpPost("RecycleLibrary")]
        public async Task<IActionResult> RecycleLibrary()
        {
            _logger.LogDebug("RecycleLibrary endpoint called - recycling library data");
            
            try
            {
                // Get library directory from saved configuration
                var libraryDir = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
                
                // Use Jellyfin-style locking that pauses instead of canceling
                var success = await Plugin.ExecuteWithLockAsync<bool>(async () =>
                {
                    _logger.LogInformation("Starting data deletion - Library directory: {LibraryDir}", libraryDir);
                    
                    // Delete all contents inside library directory if it exists
                    if (System.IO.Directory.Exists(libraryDir))
                    {
                        _logger.LogTrace("Deleting all contents inside library directory: {LibraryDir}", libraryDir);
                        
                        try
                        {
                            // Get all subdirectories and files
                            var subdirs = System.IO.Directory.GetDirectories(libraryDir);
                            var files = System.IO.Directory.GetFiles(libraryDir);
                            
                            // Delete all files in the root directory
                            foreach (var file in files)
                            {
                                System.IO.File.Delete(file);
                                _logger.LogTrace("Deleted file: {File}", file);
                            }
                            
                            // Delete all subdirectories (recursively)
                            foreach (var subdir in subdirs)
                            {
                                System.IO.Directory.Delete(subdir, true);
                                _logger.LogTrace("Deleted subdirectory: {Subdir}", subdir);
                            }
                            
                            _logger.LogInformation("Successfully deleted all contents inside library directory: {LibraryDir}", libraryDir);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to delete contents of library directory: {LibraryDir}", libraryDir);
                            throw new InvalidOperationException($"Failed to delete contents of library directory: {ex.Message}");
                        }
                    }
                    else
                    {
                        _logger.LogError("Library directory does not exist: {LibraryDir}", libraryDir);
                        throw new InvalidOperationException($"Library directory does not exist: {libraryDir}");
                    }
                    
                    _logger.LogDebug("Data deletion completed successfully");
                    
                    // Refresh the Jellyseerr library after data deletion
                    _logger.LogDebug("Starting Jellyseerr library refresh after data deletion...");
                    
                    // Call the refresh method (fire-and-await, no return value)
                    // refreshUserData defaults to true - will perform light refresh to reload user data
                    await _libraryService.RefreshBridgeLibrary(fullRefresh: true, refreshImages: true);

                    await _libraryService.ScanAllLibraries();

                    _logger.LogInformation("Jellyseerr library refresh initiated");

                    return true;
                }, _logger, "Delete Library Data");
                
                return Ok(new { 
                    success = true, 
                    message = "All Jellyseerr library data has been deleted successfully and library has been refreshed." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting library data");
                return StatusCode(500, new { 
                    error = "Failed to delete library data",
                    details = $"Data deletion failed: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }
    }
}

