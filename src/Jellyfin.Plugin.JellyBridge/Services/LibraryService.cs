using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing Jellyfin libraries with JellyBridge.
/// </summary>
public class LibraryService
{
    private readonly DebugLogger<LibraryService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;

    public LibraryService(ILogger<LibraryService> logger, ILibraryManager libraryManager, IDirectoryService directoryService)
    {
        _logger = new DebugLogger<LibraryService>(logger);
        _libraryManager = libraryManager;
        _directoryService = directoryService;
    }

    /// <summary>
    /// Refreshes the Jellyseerr library with the configured refresh options.
    /// </summary>
    /// <param name="fullRefresh">If true, performs a full metadata and image refresh. If false, only refreshes missing metadata.</param>
    public bool? RefreshJellyseerrLibrary(bool fullRefresh = true)
    {
        try
        {
            var config = Plugin.GetConfiguration();
            var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!manageJellyseerrLibrary) {
                _logger.LogDebug("[LibraryService] Jellyseerr library management is disabled");
                return null;
            }
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            _logger.LogDebug("[LibraryService] Starting Jellyseerr library refresh (FullRefresh: {FullRefresh})...", fullRefresh);

            // Find all libraries that contain Jellyseerr folders
            var libraries = _libraryManager.GetVirtualFolders();
            var jellyseerrLibraries = libraries.Where(lib => 
                lib.Locations?.Any(location => FolderUtils.IsPathInSyncDirectory(location)) == true).ToList();

            if (!jellyseerrLibraries.Any())
            {
                throw new InvalidOperationException("No Jellyseerr libraries found for refresh");
            }

            _logger.LogTrace("[LibraryService] Found {LibraryCount} Jellyseerr libraries: {LibraryNames}", 
                jellyseerrLibraries.Count, string.Join(", ", jellyseerrLibraries.Select(lib => lib.Name)));

            // Create refresh options based on fullRefresh parameter
            var refreshOptions = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = fullRefresh ? MetadataRefreshMode.FullRefresh : MetadataRefreshMode.Default,
                ImageRefreshMode = fullRefresh ? MetadataRefreshMode.FullRefresh : MetadataRefreshMode.Default,
                ReplaceAllMetadata = fullRefresh,
                ReplaceAllImages = fullRefresh,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = false,
                RemoveOldMetadata = false
            };
            
            _logger.LogTrace("[LibraryService] Refresh options - MetadataRefreshMode: {MetadataRefreshMode}, ImageRefreshMode: {ImageRefreshMode}, ReplaceAllMetadata: {ReplaceAllMetadata}, ReplaceAllImages: {ReplaceAllImages}, RegenerateTrickplay: {RegenerateTrickplay}", 
                refreshOptions.MetadataRefreshMode, refreshOptions.ImageRefreshMode, refreshOptions.ReplaceAllMetadata, refreshOptions.ReplaceAllImages, refreshOptions.RegenerateTrickplay);

            // Scan and refresh each Jellyseerr library individually in background
            var backgroundTasks = new List<Task>();
            
            foreach (var jellyseerrLibrary in jellyseerrLibraries)
            {
                var libraryFolder = _libraryManager.GetItemById(jellyseerrLibrary.ItemId);
                if (libraryFolder is Folder folder)
                {
                    _logger.LogTrace("[LibraryService] Starting background scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                    // Start the complete scan and refresh process in the background (don't await)
                    backgroundTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // First validate children to scan for new/changed files
                            _logger.LogTrace("[LibraryService] Validating library: {LibraryName}", jellyseerrLibrary.Name);
                            await folder.ValidateChildren(new Progress<double>(), refreshOptions, recursive: true, cancellationToken: CancellationToken.None);
                            
                            // Then refresh metadata with the refresh options
                            _logger.LogTrace("[LibraryService] Refreshing metadata for library: {LibraryName}", jellyseerrLibrary.Name);
                            await folder.RefreshMetadata(refreshOptions, CancellationToken.None);
                            
                            _logger.LogTrace("[LibraryService] Completed validation and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[LibraryService] Error during background scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                        }
                    }));
                }
                else
                {
                    _logger.LogWarning("[LibraryService] Library folder not found for: {LibraryName}", jellyseerrLibrary.Name);
                }
            }

            // Return immediately - background tasks continue running
            _logger.LogDebug("[LibraryService] Jellyseerr library refresh started successfully. Background scan and refresh tasks are running for {TaskCount} libraries", backgroundTasks.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LibraryService] Error refreshing Jellyseerr library");
            return false;
        }
    }

    /// <summary>
    /// Scans all Jellyfin libraries for first-time plugin initialization.
    /// Uses the same functionality as the "Scan All Libraries" button.
    /// </summary>
    public bool? ScanAllLibrariesForFirstTime()
    {
        try
        {
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!manageJellyseerrLibrary)
            {
                _logger.LogDebug("[LibraryService] Jellyseerr library management is disabled");
                return null;
            }

            _logger.LogDebug("[LibraryService] Starting full scan of all Jellyfin libraries for first-time initialization...");

            // Use the same method as the "Scan All Libraries" button, run in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _libraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
                    _logger.LogDebug("[LibraryService] Full scan of all libraries completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[LibraryService] Error during full scan of all libraries");
                }
            });

            // Return immediately - background task continues running
            _logger.LogDebug("[LibraryService] Full scan of all libraries started successfully");
            
            // Set RanFirstTime to true after successful scan
            Plugin.SetRanFirstTime(true);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LibraryService] Error scanning all libraries for first time");
            return false;
        }
    }
}
