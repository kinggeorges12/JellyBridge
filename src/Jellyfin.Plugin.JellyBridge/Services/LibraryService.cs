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
    /// <param name="createMode">If true, performs a full metadata refresh for created/updated items (ReplaceAllMetadata=true).</param>
    /// <param name="removeMode">If true, performs a refresh to detect removed items (ReplaceAllMetadata=false).</param>
    /// <param name="refreshImages">If true, refreshes images. If false, skips image refresh.</param>
    public async Task<int> RefreshBridgeLibrary(bool createMode = true, bool removeMode = true, bool refreshImages = true)
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
                IsAutomated = false,
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
                    _providerManager.QueueRefresh(id, refreshOptionsUpdate, RefreshPriority.High);
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

    /// <summary>
    /// Tests read and write access to the library directory by creating, reading, and deleting a test file.
    /// </summary>
    /// <param name="libraryDirectory">The library directory path to test. If null or empty, uses the configured default.</param>
    /// <returns>True if the test succeeds, false if any error occurs.</returns>
    public bool TestLibraryDirectoryReadWrite(string? libraryDirectory = null)
    {
        try
        {
            // Use provided directory or fall back to configured default
            var testDirectory = libraryDirectory ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            
            if (string.IsNullOrEmpty(testDirectory))
            {
                _logger.LogWarning("Library directory is not configured");
                return false;
            }

            // Ensure directory exists
            if (!Directory.Exists(testDirectory))
            {
                _logger.LogWarning("Library directory does not exist: {Directory}", testDirectory);
                return false;
            }

            var testFilePath = Path.Combine(testDirectory, ".jellybridge");
            const string testContent = "Hello World!";

            _logger.LogDebug("Testing read/write access to library directory: {Directory}", testDirectory);

            try
            {
                // Delete test file if it already exists (cleanup from previous failed test)
                if (File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                }

                // Write test file and ensure it's flushed to disk
                using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(fileStream))
                {
                    writer.Write(testContent);
                    writer.Flush();
                    fileStream.Flush(true); // Force flush to physical disk
                }

                // Read test file
                var readContent = File.ReadAllText(testFilePath);
                
                if (readContent != testContent)
                {
                    _logger.LogWarning("Test file content mismatch. Expected: {Expected}, Got: {Actual}", testContent, readContent);
                    return false;
                }

                // Delete test file
                File.Delete(testFilePath);
                
                _logger.LogDebug("Library directory read/write test successful");
                return true;
            }
            finally
            {
                // Ensure cleanup even if an exception occurs
                try
                {
                    if (File.Exists(testFilePath))
                    {
                        File.Delete(testFilePath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library directory read/write test failed");
            return false;
        }
    }
}
