using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Providers;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Jellyfin.Plugin.JellyseerrBridge.Utils;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing Jellyfin libraries with JellyseerrBridge.
/// </summary>
public class JellyseerrLibraryService
{
    private readonly JellyseerrLogger<JellyseerrLibraryService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;

    public JellyseerrLibraryService(ILogger<JellyseerrLibraryService> logger, ILibraryManager libraryManager, IDirectoryService directoryService)
    {
        _logger = new JellyseerrLogger<JellyseerrLibraryService>(logger);
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
            var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!manageJellyseerrLibrary) {
                _logger.LogDebug("[JellyseerrLibraryService] Jellyseerr library management is disabled");
                return true;
            }
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            _logger.LogDebug("[JellyseerrLibraryService] Starting Jellyseerr library refresh...");

            // Find all libraries that contain Jellyseerr folders
            var libraries = _libraryManager.GetVirtualFolders();
            var jellyseerrLibraries = libraries.Where(lib => 
                lib.Locations?.Any(location => JellyseerrFolderUtils.IsPathInSyncDirectory(location)) == true).ToList();

            if (!jellyseerrLibraries.Any())
            {
                throw new InvalidOperationException("No Jellyseerr libraries found for refresh");
            }

            _logger.LogTrace("[JellyseerrLibraryService] Found {LibraryCount} Jellyseerr libraries: {LibraryNames}", 
                jellyseerrLibraries.Count, string.Join(", ", jellyseerrLibraries.Select(lib => lib.Name)));

            // Create refresh options with default settings
            var refreshOptions = CreateDefaultRefreshOptions();
            
            _logger.LogTrace("[JellyseerrLibraryService] Refresh options - ReplaceAllMetadata: {ReplaceAllMetadata}, ReplaceAllImages: {ReplaceAllImages}, RegenerateTrickplay: {RegenerateTrickplay}", 
                refreshOptions.ReplaceAllMetadata, refreshOptions.ReplaceAllImages, refreshOptions.RegenerateTrickplay);

            // Refresh all Jellyseerr libraries
            var refreshTasks = new List<Task>();
            foreach (var jellyseerrLibrary in jellyseerrLibraries)
            {
                var libraryFolder = _libraryManager.GetItemById(jellyseerrLibrary.ItemId);
                if (libraryFolder != null)
                {
                    _logger.LogTrace("[JellyseerrLibraryService] Refreshing library: {LibraryName}", jellyseerrLibrary.Name);
                    refreshTasks.Add(libraryFolder.RefreshMetadata(refreshOptions, CancellationToken.None));
                }
                else
                {
                    _logger.LogWarning("[JellyseerrLibraryService] Library folder not found for: {LibraryName}", jellyseerrLibrary.Name);
                }
            }

            // Wait for all refreshes to complete
            await Task.WhenAll(refreshTasks);
            
            _logger.LogDebug("[JellyseerrLibraryService] Jellyseerr library refresh completed successfully");
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
}
