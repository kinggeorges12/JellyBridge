using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities;
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
    public bool RefreshJellyseerrLibrary()
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

            // Create refresh options with full refresh settings
            var refreshOptions = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ReplaceAllImages = true,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = false,
                RemoveOldMetadata = false
            };
            
            _logger.LogTrace("[JellyseerrLibraryService] Refresh options - ReplaceAllMetadata: {ReplaceAllMetadata}, ReplaceAllImages: {ReplaceAllImages}, RegenerateTrickplay: {RegenerateTrickplay}", 
                refreshOptions.ReplaceAllMetadata, refreshOptions.ReplaceAllImages, refreshOptions.RegenerateTrickplay);

            // Scan and refresh each Jellyseerr library individually in background
            var backgroundTasks = new List<Task>();
            
            foreach (var jellyseerrLibrary in jellyseerrLibraries)
            {
                var libraryFolder = _libraryManager.GetItemById(jellyseerrLibrary.ItemId);
                if (libraryFolder is Folder folder)
                {
                    _logger.LogTrace("[JellyseerrLibraryService] Starting background scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                    // Start the complete scan and refresh process in the background (don't await)
                    backgroundTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // First validate children to scan for new/changed files
                            _logger.LogTrace("[JellyseerrLibraryService] Validating library: {LibraryName}", jellyseerrLibrary.Name);
                            await folder.ValidateChildren(new Progress<double>(), refreshOptions, recursive: true, cancellationToken: CancellationToken.None);
                            
                            // Then refresh metadata with the refresh options
                            _logger.LogTrace("[JellyseerrLibraryService] Refreshing metadata for library: {LibraryName}", jellyseerrLibrary.Name);
                            await folder.RefreshMetadata(refreshOptions, CancellationToken.None);
                            
                            _logger.LogTrace("[JellyseerrLibraryService] Completed validation and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[JellyseerrLibraryService] Error during background scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                        }
                    }));
                }
                else
                {
                    _logger.LogWarning("[JellyseerrLibraryService] Library folder not found for: {LibraryName}", jellyseerrLibrary.Name);
                }
            }

            // Return immediately - background tasks continue running
            _logger.LogDebug("[JellyseerrLibraryService] Jellyseerr library refresh started successfully. Background scan and refresh tasks are running for {TaskCount} libraries", backgroundTasks.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrLibraryService] Error refreshing Jellyseerr library");
            return false;
        }
    }
}
