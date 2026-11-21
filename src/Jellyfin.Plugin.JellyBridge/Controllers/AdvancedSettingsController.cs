using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("JellyBridge")]
    public class AdvancedSettingsController : ControllerBase
    {
        private readonly DebugLogger<AdvancedSettingsController> _logger;
        private readonly RefreshService _refreshService;
        private readonly CleanupService _cleanupService;
        private readonly SyncService _syncService;

        public AdvancedSettingsController(ILoggerFactory loggerFactory, RefreshService refreshService, CleanupService cleanupService, SyncService syncService)
        {
            _logger = new DebugLogger<AdvancedSettingsController>(loggerFactory.CreateLogger<AdvancedSettingsController>());
            _refreshService = refreshService;
            _cleanupService = cleanupService;
            _syncService = syncService;
        }

        /// <summary>
        /// Cleans up metadata by removing items older than the specified number of days.
        /// </summary>
        [HttpPost("CleanupMetadata")]
        public async Task<IActionResult> CleanupMetadata()
        {
            _logger.LogInformation("Cleanup metadata requested from plugin configuration page.");
            
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
                        await _refreshService.ApplyRefreshAsync(cleanupResult);
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
        /// Recycle all JellyBridge library data.
        /// </summary>
        [HttpPost("RecycleLibrary")]
        public async Task<IActionResult> RecycleLibrary()
        {
            _logger.LogInformation("Recycle library requested from plugin configuration page.");
            
            try
            {
                // Get library directory from saved configuration
                var libraryDir = FolderUtils.GetBaseDirectory();
                _logger.LogDebug("Recycling every JellyBridge library folder; the Library Directory is {LibraryDir}", libraryDir);
                
                // Use Jellyfin-style locking that pauses instead of canceling
                var success = await Plugin.ExecuteWithLockAsync<bool>(async () =>
                {
                    _logger.LogInformation("Starting data deletion - Library directory: {LibraryDir}", libraryDir);
                    
                    _logger.LogTrace("Deleting all contents inside library directory: {LibraryDir}", libraryDir);
                    
                    try
                    {
                        // Delete the contents of every JellyBridge library folder, and of the Library Directory root
                        // itself (its leftover non-library entries), while keeping the library folders in place.
                        var bridgeDirectories = FolderUtils.GetBridgeDirectories();
                        var cleanupDirectories = bridgeDirectories
                            .Append(DiscoverLibraries.GetRootDirectory())
                            .DistinctBy(FolderUtils.GetNormalizedPath);

                        foreach (var directory in cleanupDirectories)
                        {
                            if (!Directory.Exists(directory))
                            {
                                continue;
                            }

                            foreach (var file in Directory.GetFiles(directory))
                            {
                                System.IO.File.Delete(file);
                                _logger.LogTrace("Deleted file: {File}", file);
                            }

                            foreach (var subdir in Directory.GetDirectories(directory))
                            {
                                // A subdirectory stays only when it is a library folder or contains one. Note the
                                // direction: GetNormalizedPath ends with '/', so testing the reverse would also
                                // "keep" every title folder inside a library.
                                var normalizedSubdir = FolderUtils.GetNormalizedPath(subdir);
                                if (bridgeDirectories.Any(libraryDirectory =>
                                    FolderUtils.GetNormalizedPath(libraryDirectory).StartsWith(normalizedSubdir, StringComparison.Ordinal)))
                                {
                                    _logger.LogDebug("Kept library folder: {Subdir}", subdir);
                                    continue;
                                }

                                System.IO.Directory.Delete(subdir, true);
                                _logger.LogTrace("Deleted subdirectory: {Subdir}", subdir);
                            }
                        }
                        
                        _logger.LogInformation("Successfully deleted all contents of the JellyBridge libraries");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete contents of the JellyBridge libraries");
                        throw new InvalidOperationException($"Failed to delete contents of the JellyBridge libraries: {ex.Message}");
                    }
                    
                    // Refresh the JellyBridge library after data deletion
                    _logger.LogDebug("Starting JellyBridge library refresh after data deletion...");

                    // Call the refresh method (fire-and-await, no return value)
                    // Update refresh always runs to reload user data (play counts)
                    _refreshService.ScanThenRefreshRunner(createMode: false, removeMode: true, refreshImages: false, force: true);
                    
                    _logger.LogInformation("JellyBridge library refresh initiated");

                    return true;
                }, _logger, "Delete Library Data");
                
                return Ok(new { 
                    success = true, 
                    message = "All JellyBridge library data has been deleted successfully and library has been refreshed." 
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

