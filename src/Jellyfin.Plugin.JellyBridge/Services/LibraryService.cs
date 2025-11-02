using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Library;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
 

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing Jellyfin libraries with JellyBridge.
/// </summary>
public class LibraryService
{
    private readonly DebugLogger<LibraryService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;
    private readonly JellyfinIProviderManager _providerManager;

    public LibraryService(ILogger<LibraryService> logger, JellyfinILibraryManager libraryManager, IDirectoryService directoryService, JellyfinIProviderManager providerManager)
    {
        _logger = new DebugLogger<LibraryService>(logger);
        _libraryManager = libraryManager;
        _directoryService = directoryService;
        _providerManager = providerManager;
    }

    /// <summary>
    /// Refreshes the Jellyseerr library with the configured refresh options.
    /// </summary>
    /// <param name="fullRefresh">If true, performs a full metadata and image refresh. If false, only refreshes missing metadata.</param>
    /// <param name="refreshUserData">If true, performs a light refresh to reload user data (play counts). If false, uses standard refresh.</param>
    /// <param name="refreshImages">If true, refreshes images. If false, skips image refresh.</param>
    public async Task<int> RefreshBridgeLibrary(bool fullRefresh = true, bool refreshUserData = true, bool refreshImages = true)
    {
        var queuedCount = 0;
        try
        {
            var config = Plugin.GetConfiguration();
            var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!manageJellyseerrLibrary) {
                _logger.LogDebug("Jellyseerr library management is disabled");
                return queuedCount;
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

            // Scan for new and updated files
            // Refresh?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default&ReplaceAllImages=false&RegenerateTrickplay=false&ReplaceAllMetadata=false
            // Create refresh options for refreshing user data - minimal refresh to reload user data like play counts
            var refreshOptionsUpdate = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ImageRefreshMode = MetadataRefreshMode.Default,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = false,
                RemoveOldMetadata = false
            };

            // Search for missing metadata
            // Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=FullRefresh&ReplaceAllImages=false&RegenerateTrickplay=false&ReplaceAllMetadata=false
            // Create refresh options for creating or updating items - replace all metadata
            var refreshOptionsCreate = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ReplaceAllImages = refreshImages,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = false,
                RemoveOldMetadata = false
            };

            // Replace all metadata
            // Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=FullRefresh&ReplaceAllImages=true&RegenerateTrickplay=false&ReplaceAllMetadata=true
            // Create refresh options for refreshing removed items - search for missing metadata only
            var refreshOptionsRemove = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ReplaceAllImages = refreshImages,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = false,
                RemoveOldMetadata = false
            };
            
            _logger.LogTrace("Refresh options - Create: Metadata={CreateMeta}, Images={CreateImages}, ReplaceAllMetadata={CreateReplaceMeta}, ReplaceAllImages={CreateReplaceImages}, RegenerateTrickplay={CreateTrick}; Remove: Metadata={RemoveMeta}, Images={RemoveImages}, ReplaceAllMetadata={RemoveReplaceMeta}, ReplaceAllImages={RemoveReplaceImages}, RegenerateTrickplay={RemoveTrick}; Update: Metadata={UpdateMeta}, Images={UpdateImages}, ReplaceAllMetadata={UpdateReplaceMeta}, ReplaceAllImages={UpdateReplaceImages}, RegenerateTrickplay={UpdateTrick}",
                refreshOptionsCreate.MetadataRefreshMode, refreshOptionsCreate.ImageRefreshMode, refreshOptionsCreate.ReplaceAllMetadata, refreshOptionsCreate.ReplaceAllImages, refreshOptionsCreate.RegenerateTrickplay,
                refreshOptionsRemove.MetadataRefreshMode, refreshOptionsRemove.ImageRefreshMode, refreshOptionsRemove.ReplaceAllMetadata, refreshOptionsRemove.ReplaceAllImages, refreshOptionsRemove.RegenerateTrickplay,
                refreshOptionsUpdate.MetadataRefreshMode, refreshOptionsUpdate.ImageRefreshMode, refreshOptionsUpdate.ReplaceAllMetadata, refreshOptionsUpdate.ReplaceAllImages, refreshOptionsUpdate.RegenerateTrickplay);

            // Queue provider refresh for each Jellyseerr library via ProviderManager (same as API behavior)
            foreach (var jellyseerrLibrary in jellyseerrLibraries)
            {
                var libraryFolder = _libraryManager.Inner.GetItemById(Guid.Parse(jellyseerrLibrary.ItemId));
                if (libraryFolder != null)
                {
                    _logger.LogTrace("Starting scan and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                    // First validate children to scan for new/changed files
                    _logger.LogTrace("Validating library: {LibraryName}", jellyseerrLibrary.Name);
                    //await ((dynamic)libraryFolder).ValidateChildren(new Progress<double>(), refreshOptions, recursive: true, cancellationToken: CancellationToken.None);

                    if (refreshUserData)
                    {
                        // Standard refresh for metadata/image updates
                        _providerManager.QueueRefresh(libraryFolder.Id, refreshOptionsCreate, RefreshPriority.High);
                        if(fullRefresh)
                        {
                            _providerManager.QueueRefresh(libraryFolder.Id, refreshOptionsRemove, RefreshPriority.High);
                        }
                    }
                    else
                    {
                        // Light refresh for user data updates (play counts)
                        _logger.LogTrace("Using update refresh options for library: {LibraryName}", jellyseerrLibrary.Name);
                        _providerManager.QueueRefresh(libraryFolder.Id, refreshOptionsUpdate, RefreshPriority.High);
                    }
                    // ValidateChildren already refreshes child metadata when recursive=true.
                    // If needed in future, we could call RefreshMetadata here.

                    _logger.LogTrace("Completed validation and refresh for library: {LibraryName}", jellyseerrLibrary.Name);
                }
                else
                {
                    _logger.LogWarning("Library folder not found for: {LibraryName}", jellyseerrLibrary.Name);
                    continue;
                }

                queuedCount++;
                _logger.LogTrace("Queued provider refresh for library: {LibraryName} ({ItemId})", jellyseerrLibrary.Name, libraryFolder?.Id);
            }

            _logger.LogDebug("Queued provider refresh for {Count} Jellyseerr libraries", queuedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Jellyseerr library");
        }
        // No-op await to satisfy async method requirement when no asynchronous operations are performed
        await Task.CompletedTask;
        return queuedCount;
    }

    /// <summary>
    /// Scans all Jellyfin libraries for first-time plugin initialization.
    /// Uses the same functionality as the "Scan All Libraries" button.
    /// </summary>
    public async Task<bool?> ScanAllLibraries()
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

            // Use the same method as the "Scan All Libraries" button
                    await _libraryManager.Inner.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);

            _logger.LogDebug("Full scan of all libraries completed successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning all libraries for first time");
            return false;
        }
    }
}
