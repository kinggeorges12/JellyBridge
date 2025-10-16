using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Providers;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing Jellyfin libraries with JellyseerrBridge.
/// </summary>
public class JellyseerrLibraryService
{
    private readonly ILogger<JellyseerrLibraryService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;

    public JellyseerrLibraryService(ILogger<JellyseerrLibraryService> logger, ILibraryManager libraryManager, IDirectoryService directoryService)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _directoryService = directoryService;
    }

    /// <summary>
    /// Refreshes the Jellyseerr library with the configured refresh options.
    /// </summary>
    public async Task<bool> RefreshJellyseerrLibraryAsync()
    {
        try
        {
            var config = Plugin.GetConfiguration();
            var syncDirectory = config?.LibraryDirectory;

            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("[JellyseerrLibraryService] Cannot refresh library - sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return false;
            }

            _logger.LogInformation("[JellyseerrLibraryService] Starting Jellyseerr library refresh...");

            // Find the Jellyseerr library
            var libraries = _libraryManager.GetVirtualFolders();
            var jellyseerrLibrary = libraries.FirstOrDefault(lib => 
                lib.Locations?.Any(location => JellyseerrFolderUtils.IsPathInSyncDirectory(location)) == true ||
                lib.Name?.Contains("Jellyseerr", StringComparison.OrdinalIgnoreCase) == true ||
                lib.Name?.Contains("Bridge", StringComparison.OrdinalIgnoreCase) == true);

            if (jellyseerrLibrary == null)
            {
                _logger.LogWarning("[JellyseerrLibraryService] Jellyseerr library not found for refresh");
                return false;
            }

            _logger.LogInformation("[JellyseerrLibraryService] Found Jellyseerr library: {LibraryName}", jellyseerrLibrary.Name);

            // Create refresh options with default settings
            var refreshOptions = CreateDefaultRefreshOptions();
            
            _logger.LogInformation("[JellyseerrLibraryService] Refresh options - ReplaceAllMetadata: {ReplaceAllMetadata}, ReplaceAllImages: {ReplaceAllImages}, RegenerateTrickplay: {RegenerateTrickplay}", 
                refreshOptions.ReplaceAllMetadata, refreshOptions.ReplaceAllImages, refreshOptions.RegenerateTrickplay);

            // Get the root folder for the Jellyseerr library
            var rootFolder = _libraryManager.RootFolder;
            if (rootFolder == null)
            {
                _logger.LogError("[JellyseerrLibraryService] Root folder not found");
                return false;
            }

            // Refresh the library
            await rootFolder.RefreshMetadata(CancellationToken.None);
            
            _logger.LogInformation("[JellyseerrLibraryService] Jellyseerr library refresh completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrLibraryService] Error refreshing Jellyseerr library");
            return false;
        }
    }

    /// <summary>
    /// Creates default MetadataRefreshOptions for library refresh.
    /// </summary>
    /// <returns>Configured MetadataRefreshOptions instance.</returns>
    private MetadataRefreshOptions CreateDefaultRefreshOptions()
    {
        return new MetadataRefreshOptions(_directoryService)
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllMetadata = true,
            ReplaceAllImages = true,
            RegenerateTrickplay = false,
            ForceSave = true
        };
    }

    /// <summary>
    /// Validates that the Jellyseerr library is properly configured and accessible.
    /// </summary>
    /// <returns>True if the library is valid, false otherwise.</returns>
    public Task<bool> ValidateJellyseerrLibraryAsync()
    {
        try
        {
            var config = Plugin.GetConfiguration();
            var syncDirectory = config?.LibraryDirectory;

            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("[JellyseerrLibraryService] Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return Task.FromResult(false);
            }

            // Find the Jellyseerr library
            var libraries = _libraryManager.GetVirtualFolders();
            var jellyseerrLibrary = libraries.FirstOrDefault(lib => 
                lib.Locations?.Any(location => JellyseerrFolderUtils.IsPathInSyncDirectory(location)) == true ||
                lib.Name?.Contains("Jellyseerr", StringComparison.OrdinalIgnoreCase) == true ||
                lib.Name?.Contains("Bridge", StringComparison.OrdinalIgnoreCase) == true);

            if (jellyseerrLibrary == null)
            {
                _logger.LogWarning("[JellyseerrLibraryService] Jellyseerr library not found");
                return Task.FromResult(false);
            }

            _logger.LogInformation("[JellyseerrLibraryService] Jellyseerr library validation successful: {LibraryName}", jellyseerrLibrary.Name);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrLibraryService] Error validating Jellyseerr library");
            return Task.FromResult(false);
        }
    }
}
