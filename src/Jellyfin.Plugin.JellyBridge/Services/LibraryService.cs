using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Library;
 

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing Jellyfin libraries with JellyBridge.
/// </summary>
public class LibraryService
{
    private readonly DebugLogger<LibraryService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;

    public LibraryService(ILogger<LibraryService> logger, JellyfinILibraryManager libraryManager, IDirectoryService directoryService)
    {
        _logger = new DebugLogger<LibraryService>(logger);
        _libraryManager = libraryManager;
        _directoryService = directoryService;
    }

    /// <summary>
    /// Refreshes the Jellyseerr library with the configured refresh options.
    /// </summary>
    /// <param name="fullRefresh">If true, performs a full metadata and image refresh. If false, only refreshes missing metadata.</param>
    public bool? RefreshJellyseerrLibrary(bool fullRefresh, bool refreshImages = true)
    {
        try
        {
            var config = Plugin.GetConfiguration();
            var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!manageJellyseerrLibrary) {
                _logger.LogDebug("Jellyseerr library management is disabled");
                return null;
            }
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            _logger.LogDebug("Starting Jellyseerr library refresh (FullRefresh: {FullRefresh})...", fullRefresh);

            // Find all libraries that contain Jellyseerr folders
            var libraries = _libraryManager.Inner.GetVirtualFolders();
            var jellyseerrLibraries = libraries.Where(lib => 
                lib.Locations?.Any(location => FolderUtils.IsPathInSyncDirectory(location)) == true).ToList();

            if (!jellyseerrLibraries.Any())
            {
                throw new InvalidOperationException("No Jellyseerr libraries found for refresh");
            }

            _logger.LogTrace("Found {LibraryCount} Jellyseerr libraries: {LibraryNames}", 
                jellyseerrLibraries.Count, string.Join(", ", jellyseerrLibraries.Select(lib => lib.Name)));

            // Create refresh options based on fullRefresh parameter
            var refreshOptions = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ImageRefreshMode = MetadataRefreshMode.Default,
                ReplaceAllMetadata = fullRefresh,
                ReplaceAllImages = refreshImages,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = true,
                RemoveOldMetadata = fullRefresh
            };
            
            _logger.LogTrace("Refresh options - MetadataRefreshMode: {MetadataRefreshMode}, ImageRefreshMode: {ImageRefreshMode}, ReplaceAllMetadata: {ReplaceAllMetadata}, ReplaceAllImages: {ReplaceAllImages}, RegenerateTrickplay: {RegenerateTrickplay}", 
                refreshOptions.MetadataRefreshMode, refreshOptions.ImageRefreshMode, refreshOptions.ReplaceAllMetadata, refreshOptions.ReplaceAllImages, refreshOptions.RegenerateTrickplay);

            // Scan and refresh each Jellyseerr library individually in background
            var backgroundTasks = new List<Task>();
            
            foreach (var jellyseerrLibrary in jellyseerrLibraries)
            {
                var libraryFolder = _libraryManager.Inner.GetItemById(Guid.Parse(jellyseerrLibrary.ItemId));
                if (libraryFolder != null)
                {
                    _logger.LogTrace("Starting background scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                    // Start the complete scan and refresh process in the background (don't await)
                    backgroundTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // First validate children to scan for new/changed files
                            _logger.LogTrace("Validating library: {LibraryName}", jellyseerrLibrary.Name);
                            await ((dynamic)libraryFolder).ValidateChildren(new Progress<double>(), refreshOptions, recursive: true, cancellationToken: CancellationToken.None);
                            
                            // Then refresh metadata with the refresh options
                            _logger.LogTrace("Refreshing metadata for library: {LibraryName}", jellyseerrLibrary.Name);
                            await ((dynamic)libraryFolder).RefreshMetadata(refreshOptions, CancellationToken.None);
                            
                            _logger.LogTrace("Completed validation and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during background scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                        }
                    }));
                }
                else
                {
                    _logger.LogWarning("Library folder not found for: {LibraryName}", jellyseerrLibrary.Name);
                }
            }

            // Return immediately - background tasks continue running
            _logger.LogDebug("Jellyseerr library refresh started successfully. Background scan and refresh tasks are running for {TaskCount} libraries", backgroundTasks.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Jellyseerr library");
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
                _logger.LogDebug("Jellyseerr library management is disabled");
                return null;
            }

            _logger.LogDebug("Starting full scan of all Jellyfin libraries for first-time initialization...");

            // Use the same method as the "Scan All Libraries" button, run in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _libraryManager.Inner.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
                    _ = RefreshJellyseerrLibrary(fullRefresh: true, refreshImages: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during full scan of all libraries");
                }
            });

            // Return immediately - background task continues running
            _logger.LogDebug("Full scan of all libraries started successfully");
            
            // Set RanFirstTime to true after successful scan
            Plugin.SetRanFirstTime(true);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning all libraries for first time");
            return false;
        }
    }
}
