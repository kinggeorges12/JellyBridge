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
    public class AdvancedSettingsController : ControllerBase
    {
        private readonly DebugLogger<AdvancedSettingsController> _logger;
        private readonly LibraryService _libraryService;
        private readonly CleanupService _cleanupService;
        private readonly SyncService _syncService;

        public AdvancedSettingsController(ILoggerFactory loggerFactory, LibraryService libraryService, CleanupService cleanupService, SyncService syncService)
        {
            _logger = new DebugLogger<AdvancedSettingsController>(loggerFactory.CreateLogger<AdvancedSettingsController>());
            _libraryService = libraryService;
            _cleanupService = cleanupService;
            _syncService = syncService;
        }

        /// <summary>
        /// Cleans up metadata by removing items older than the specified number of days.
        /// </summary>
        [HttpPost("CleanupMetadata")]
        public async Task<IActionResult> CleanupMetadata()
        {
            _logger.LogTrace("Cleanup metadata requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    var cleanupResult = await _cleanupService.CleanupMetadataAsync();

                    _logger.LogInformation("Cleanup metadata completed: {DeletedCount} items deleted, {MoviesCleaned} movie folders, {ShowsCleaned} show folders without metadata", 
                        cleanupResult.ItemsDeleted.Count, cleanupResult.MoviesCleaned, cleanupResult.ShowsCleaned);

                    // Apply refresh if cleanup removed items
                    if (cleanupResult.Refresh != null)
                    {
                        _logger.LogDebug("Applying cleanup refresh plan (CreateRefresh: {CreateRefresh}, RemoveRefresh: {RemoveRefresh}, RefreshImages: {RefreshImages})", 
                            cleanupResult.Refresh.CreateRefresh, cleanupResult.Refresh.RemoveRefresh, cleanupResult.Refresh.RefreshImages);
                        _logger.LogDebug("Awaiting scan of all Jellyfin libraries...");
                        await _syncService.ApplyRefreshAsync(cleanupResult: cleanupResult);
                        _logger.LogDebug("Scan of all libraries completed");
                    }

                    return new
                    {
                        result = cleanupResult.ToString(),
                        success = cleanupResult.Success,
                        message = cleanupResult.Message
                    };
                }, _logger, "Cleanup Metadata");

                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Cleanup metadata timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Cleanup metadata operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup metadata failed");
                return StatusCode(500, new { 
                    success = false,
                    error = "Cleanup failed",
                    message = $"Cleanup operation failed: {ex.Message}", 
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
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
                var libraryDir = FolderUtils.GetBaseDirectory();
                
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
                    // Update refresh always runs to reload user data (play counts)
                    await _libraryService.RefreshBridgeLibrary(createMode: true, removeMode: true, refreshImages: true);

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

