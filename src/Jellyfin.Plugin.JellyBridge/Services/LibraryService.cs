using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using MediaBrowser.Controller.Providers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Globalization;

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
    private readonly ITaskManager _taskManager;
    private readonly ILocalizationManager _localization;
    public LibraryService(ILogger<LibraryService> logger, JellyfinILibraryManager libraryManager, IDirectoryService directoryService, JellyfinIProviderManager providerManager, ITaskManager taskManager, ILocalizationManager localization)
    {
        _logger = new DebugLogger<LibraryService>(logger);
        _libraryManager = libraryManager;
        _directoryService = directoryService;
        _providerManager = providerManager;
        _taskManager = taskManager;
        _localization = localization;
    }

    /// <summary>
    /// Refreshes the Jellyseerr library with the configured refresh options.
    /// </summary>
    /// <param name="createMode">If true, performs a full metadata refresh for created/updated items (ReplaceAllMetadata=true).</param>
    /// <param name="removeMode">If true, performs a refresh to detect removed items (ReplaceAllMetadata=false).</param>
    /// <param name="refreshImages">If true, refreshes images. If false, skips image refresh.</param>
    public async Task<int> RefreshBridgeLibrary(bool createMode = true, bool removeMode = true, bool refreshImages = true)
    {
        var queuedCount = 0;
        try
        {
            var config = Plugin.GetConfiguration();
            var syncDirectory = FolderUtils.GetBaseDirectory();
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!manageJellyseerrLibrary) {
                _logger.LogDebug("Jellyseerr library management is disabled");
                return queuedCount;
            }
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            _logger.LogDebug("Starting Jellyseerr library refresh (CreateMode: {CreateMode}, RemoveMode: {RemoveMode})...", createMode, removeMode);

            // Find all libraries that contain JellyBridge folders
            var libraries = _libraryManager.Inner.GetVirtualFolders();
            var bridgeLibraries = libraries.Where(lib => 
                lib.Locations?.Any(location => FolderUtils.IsPathInSyncDirectory(location)) == true).ToList();

            if (!bridgeLibraries.Any())
            {
                throw new InvalidOperationException("No JellyBridge libraries found for refresh");
            }

            _logger.LogTrace("Found {LibraryCount} JellyBridge libraries: {LibraryNames}", 
                bridgeLibraries.Count, string.Join(", ", bridgeLibraries.Select(lib => lib.Name)));

            // Remove ignored items
            // Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=FullRefresh&ReplaceAllImages=false&RegenerateTrickplay=false&ReplaceAllMetadata=false
            // Create refresh options for refreshing removed items - search for missing metadata only
            var refreshOptionsRemove = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false,
                RegenerateTrickplay = false,
                ForceSave = true,
                IsAutomated = true,
                RemoveOldMetadata = false
            };

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
                IsAutomated = true,
                RemoveOldMetadata = false
            };

            // Search for missing metadata
            // Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=FullRefresh&ReplaceAllImages=true&RegenerateTrickplay=false&ReplaceAllMetadata=true
            // Create refresh options for creating or updating items - replace all metadata
            var refreshOptionsCreate = new MetadataRefreshOptions(_directoryService)
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

            // Collect valid library folders first
            var validLibraryFolders = new List<(string name, Guid id)>();
            foreach (var bridgeLibrary in bridgeLibraries)
            {
                try
                {
                    // Validate ItemId before parsing
                    if (string.IsNullOrEmpty(bridgeLibrary.ItemId))
                    {
                        throw new InvalidOperationException("Library has null or empty ItemId");
                    }

                    // ItemId is a string property containing a GUID, so we need to parse it
                    var libraryItemId = Guid.Parse(bridgeLibrary.ItemId);

                    var libraryFolder = _libraryManager.Inner.GetItemById(libraryItemId);
                    if (libraryFolder == null)
                    {
                        throw new InvalidOperationException("Library folder not found");
                    }

                    validLibraryFolders.Add((bridgeLibrary.Name, libraryFolder.Id));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing library '{LibraryName}', continuing with remaining libraries", bridgeLibrary.Name);
                }
            }

            // Queue all Remove refreshes first (if removeMode is enabled)
            if (removeMode)
            {
                _logger.LogTrace("Queueing Remove refreshes for {Count} libraries", validLibraryFolders.Count);
                foreach (var (name, id) in validLibraryFolders)
                {
                    try
                    {
                        _providerManager.QueueRefresh(id, refreshOptionsRemove, RefreshPriority.High);
                        _logger.LogTrace("Queued Remove refresh for library: {LibraryName} ({ItemId})", name, id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error queueing Remove refresh for library '{LibraryName}'", name);
                    }
                }
            }

            // Queue all Update refreshes
            _logger.LogTrace("Queueing Update refreshes for {Count} libraries", validLibraryFolders.Count);
            foreach (var (name, id) in validLibraryFolders)
            {
                try
                {
                    _providerManager.QueueRefresh(id, refreshOptionsUpdate, RefreshPriority.Normal);
                    _logger.LogTrace("Queued Update refresh for library: {LibraryName} ({ItemId})", name, id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error queueing Update refresh for library '{LibraryName}'", name);
                }
            }

            // Queue all Create refreshes (if createMode is enabled)
            if (createMode)
            {
                _logger.LogTrace("Queueing Create refreshes for {Count} libraries", validLibraryFolders.Count);
                foreach (var (name, id) in validLibraryFolders)
                {
                    try
                    {
                        _providerManager.QueueRefresh(id, refreshOptionsCreate, RefreshPriority.Low);
                        _logger.LogTrace("Queued Create refresh for library: {LibraryName} ({ItemId})", name, id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error queueing Create refresh for library '{LibraryName}'", name);
                    }
                }
            }

            queuedCount = validLibraryFolders.Count;

            _logger.LogDebug("Queued provider refresh for {Count} JellyBridge libraries", queuedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing JellyBridge library");
        }
        // No-op await to satisfy async method requirement when no asynchronous operations are performed
        await Task.CompletedTask;
        return queuedCount;
    }

    private async Task<bool> WaitForTaskRefreshLibrary()
    {
        // Task is stored as the localized name
        var localizedTaskName = _localization.GetLocalizedString("TaskRefreshLibrary");

        // Find the task worker
        var taskWorker = _taskManager.ScheduledTasks.FirstOrDefault(t => t.Name == localizedTaskName);

        if (taskWorker == null)
        {
            _logger.LogWarning($"{localizedTaskName} task not found.");
            return false;
        }

        // Wait for it to complete by checking the state
        var timeout = DateTime.UtcNow.AddMinutes(30);
        var taskFinished = false;
        while (DateTime.UtcNow < timeout)
        {
            // get the State property
            var state = taskWorker.State;
            
            // If it's not running or queued, it's done
            if (state != TaskState.Running)
            {
                _logger.LogTrace($"Task completed with state: {state}");
                taskFinished = true;
                break;
            }
            
            _logger.LogTrace($"Task state: {state}, waiting...");
            await Task.Delay(1000);
        }

        if (taskFinished == false)
        {
            _logger.LogWarning("Scan timed out after 30 minutes");
            return false;
        }
        return taskFinished;
    }

    /// <summary>
    /// Scans all Jellyfin libraries for first-time plugin initialization.
    /// Uses the same functionality as the "Scan All Libraries" button.
    /// </summary>
    public async Task<bool?> ScanAllLibraries(bool force=false)
    {
        var libraryDir = FolderUtils.GetBaseDirectory();
        var tempDir = Path.Combine(libraryDir, "_blank");
        try
        {
            var manageJellyseerrLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyseerrLibrary));

            if (!force && !manageJellyseerrLibrary)
            {
                _logger.LogDebug("Jellyseerr library management is disabled");
                return null;
            }

            if (force)
            {
                // Create temp directory to force refresh
                Directory.CreateDirectory(tempDir);
                File.Create(Path.Combine(tempDir, ".ignore")).Close();
            }

            _logger.LogDebug("Starting full scan of all Jellyfin libraries for first-time initialization...");

            // Wait for scan before and after running a refresh
            await WaitForTaskRefreshLibrary();
            // Use the same method as the "Scan All Libraries" button
            await _libraryManager.Inner.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
            await WaitForTaskRefreshLibrary();
            
            _logger.LogDebug("Full scan of all libraries completed successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning all libraries for first time");
            return false;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Scans all libraries and then refreshes the JellyBridge library in the background.
    /// If force is true, creates an ignore file to run the refresh on an empty library.
    /// </summary>
    public void ScanThenRefreshRunner(bool createMode, bool removeMode, bool refreshImages, bool force=false)
    {
        // Fire and forget: First scan, THEN refresh in background
        _ = Task.Run(async () =>
        {
            try
            {
                
                _logger.LogDebug("Starting background scan of all Jellyfin libraries...");
                await ScanAllLibraries(force: force);
                
                _logger.LogDebug("Applying refresh plan - CreateMode: {CreateMode}, RemoveMode: {RemoveMode}, RefreshImages: {RefreshImages}", createMode, removeMode, refreshImages);
                await RefreshBridgeLibrary(createMode: createMode, removeMode: removeMode, refreshImages: refreshImages);
                _logger.LogDebug("Background refresh completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background refresh operations");
            }
        });
    }
}
