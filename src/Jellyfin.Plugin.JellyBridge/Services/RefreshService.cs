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
using MediaBrowser.Model.IO;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using System.Reflection;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing Jellyfin libraries with JellyBridge.
/// </summary>
public class RefreshService
{
    private readonly DebugLogger<RefreshService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly JellyfinIProviderManager _providerManager;
    private readonly ITaskManager _taskManager;
    private readonly ILocalizationManager _localization;
    private readonly IServiceProvider _serviceProvider;
    // 8b fix 2: the .ignore rule instances + their ClearDirectoryCache method, resolved once on first use —
    // the fire-and-forget ScanThenRefreshRunner calls the clear after the request scope may already be disposed.
    // 8c fix 2: static, so every RefreshService instance (the service is AddTransient) shares one resolved
    // set of rule instances and one failed scope can never poison another instance's clear.
    private static List<object>? _dotIgnoreRules;
    private static MethodInfo? _dotIgnoreClear;
    public RefreshService(ILogger<RefreshService> logger, JellyfinILibraryManager libraryManager, IFileSystem fileSystem, JellyfinIProviderManager providerManager, ITaskManager taskManager, ILocalizationManager localization, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = new DebugLogger<RefreshService>(logger);
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _providerManager = providerManager;
        _taskManager = taskManager;
        _localization = localization;
    }

    /// <summary>
    /// Applies the post-sync refresh operations using a single sync result.
    /// - Calls RefreshBridgeLibrary with computed parameters
    /// </summary>
    public async Task ApplyRefreshAsync(RefreshableResult? results)
    {
        await ApplyRefreshAsync( [results] );
    }

    /// <summary>
    /// Applies the post-sync refresh operations based on the two sync results.
    /// - Calls RefreshBridgeLibrary with computed parameters
    /// - Then scans all libraries and awaits completion
    /// - Safety net (task 7): afterwards, every bridge library that Jellyfin shows with 0 items while its
    ///   folder holds title folders gets a targeted refresh — that is the "new library stayed empty" state:
    ///   the scan-all plan is skipped while Jellyfin's Scan Media Library task is already running.
    /// </summary>
    public async Task ApplyRefreshAsync(List<RefreshableResult?> results)
    {
        await ApplyRefreshPlanAsync(results);
        RefreshEmptyBridgeLibraries();
    }

    /// <summary>
    /// The actual refresh-plan application, see <see cref="ApplyRefreshAsync"/>.
    /// </summary>
    private async Task ApplyRefreshPlanAsync(List<RefreshableResult?> results)
    {
        List<RefreshableResult> allResults = results.OfType<RefreshableResult>().ToList();
        
        // Refresh when any step asked for it (e.g. a scheduled sync that added items but had no new favorites)
        if (!allResults.Any(r => r.Refresh != null))
        {
            _logger.LogDebug("No refresh plan applied");
            return;
        }

        // Make Jellyfin see .ignore files created/removed since its last scan
        ClearDotIgnoreCache();

        var scanAll = allResults.Any(r => r.Refresh?.ScanAllLibraries == true);
        var createMode = allResults.Any(r => r.Refresh?.CreateRefresh == true);
        var removeMode = allResults.Any(r => r.Refresh?.RemoveRefresh == true);
        var refreshImages = allResults.Any(r => r.Refresh?.RefreshImages == true);

        // Call the refresh method (fire-and-await, no return value)
        // Update refresh always runs to reload user data (play counts)
        if (scanAll)
        {
            ScanThenRefreshRunner(createMode: createMode, removeMode: removeMode, refreshImages: refreshImages);
        }
        else
        {
            await RefreshBridgeLibrary(createMode: createMode, removeMode: removeMode, refreshImages: refreshImages);
        }
            
        _logger.LogInformation("JellyBridge library refresh initiated");
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
            var manageJellyBridgeLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyBridgeLibrary));

            if (!manageJellyBridgeLibrary) {
                _logger.LogDebug("Jellyseerr library management is disabled");
                return queuedCount;
            }

            _logger.LogDebug("Starting Jellyseerr library refresh (CreateMode: {CreateMode}, RemoveMode: {RemoveMode})...", createMode, removeMode);

            // Find all libraries that contain JellyBridge folders
            var libraries = _libraryManager.Inner.GetVirtualFolders();
            var bridgeLibraries = libraries.Where(lib => 
                lib.Locations?.Any(location => FolderUtils.IsPathInAnyBridgeDirectory(location)) == true).ToList();

            if (!bridgeLibraries.Any())
            {
                throw new InvalidOperationException("No JellyBridge libraries found for refresh");
            }

            _logger.LogTrace("Found {LibraryCount} JellyBridge libraries: {LibraryNames}", 
                bridgeLibraries.Count, string.Join(", ", bridgeLibraries.Select(lib => lib.Name)));

            // Remove ignored items
            // Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=FullRefresh&ReplaceAllImages=false&RegenerateTrickplay=false&ReplaceAllMetadata=false
            // Create refresh options for refreshing removed items - search for missing metadata only
            var refreshOptionsRemove = BuildRemoveRefreshOptions();

            // Scan for new and updated files
            // Refresh?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default&ReplaceAllImages=false&RegenerateTrickplay=false&ReplaceAllMetadata=false
            // Create refresh options for refreshing user data - minimal refresh to reload user data like play counts
            var refreshOptionsUpdate = BuildUpdateRefreshOptions();

            // Search for missing metadata
            // Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=FullRefresh&ReplaceAllImages=true&RegenerateTrickplay=false&ReplaceAllMetadata=true
            // Create refresh options for creating or updating items - replace all metadata
            var refreshOptionsCreate = BuildCreateRefreshOptions(refreshImages);
            
            _logger.LogTrace("Refresh options - Create: Metadata={CreateMeta}, Images={CreateImages}, ReplaceAllMetadata={CreateReplaceMeta}, ReplaceAllImages={CreateReplaceImages}, RegenerateTrickplay={CreateTrick}; Remove: Metadata={RemoveMeta}, Images={RemoveImages}, ReplaceAllMetadata={RemoveReplaceMeta}, ReplaceAllImages={RemoveReplaceImages}, RegenerateTrickplay={RemoveTrick}; Update: Metadata={UpdateMeta}, Images={UpdateImages}, ReplaceAllMetadata={UpdateReplaceMeta}, ReplaceAllImages={UpdateReplaceImages}, RegenerateTrickplay={UpdateTrick}",
                refreshOptionsCreate.MetadataRefreshMode, refreshOptionsCreate.ImageRefreshMode, refreshOptionsCreate.ReplaceAllMetadata, refreshOptionsCreate.ReplaceAllImages, refreshOptionsCreate.RegenerateTrickplay,
                refreshOptionsRemove.MetadataRefreshMode, refreshOptionsRemove.ImageRefreshMode, refreshOptionsRemove.ReplaceAllMetadata, refreshOptionsRemove.ReplaceAllImages, refreshOptionsRemove.RegenerateTrickplay,
                refreshOptionsUpdate.MetadataRefreshMode, refreshOptionsUpdate.ImageRefreshMode, refreshOptionsUpdate.ReplaceAllMetadata, refreshOptionsUpdate.ReplaceAllImages, refreshOptionsUpdate.RegenerateTrickplay);

            // Collect valid library folders first
            var validLibraryFolders = new List<(string name, MediaBrowser.Controller.Entities.Folder folder)>();
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
                    
                    validLibraryFolders.Add((bridgeLibrary.Name, (MediaBrowser.Controller.Entities.Folder)libraryFolder));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing library '{LibraryName}', continuing with remaining libraries", bridgeLibrary.Name);
                }
            }

            /*
            // Queue all Remove refreshes first (if removeMode is enabled)
            if (removeMode)
            {
                _logger.LogTrace("Queueing Remove refreshes for {Count} libraries", validLibraryFolders.Count);
                foreach (var (name, folder) in validLibraryFolders)
                {
                    try
                    {
                        _providerManager.QueueRefresh(folder.Id, refreshOptionsRemove, RefreshPriority.High);
                        _logger.LogTrace("Queued Remove refresh for library: {LibraryName} ({ItemId})", name, folder.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error queueing Remove refresh for library '{LibraryName}'", name);
                    }
                }
            }

            // Queue all Update refreshes
            _logger.LogTrace("Queueing Update refreshes for {Count} libraries", validLibraryFolders.Count);
            foreach (var (name, folder) in validLibraryFolders)
            {
                try
                {
                    _providerManager.QueueRefresh(folder.Id, refreshOptionsUpdate, RefreshPriority.Normal);
                    _logger.LogTrace("Queued Update refresh for library: {LibraryName} ({ItemId})", name, folder.Id);
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
                foreach (var (name, folder) in validLibraryFolders)
                {
                    try
                    {
                        _providerManager.QueueRefresh(folder.Id, refreshOptionsCreate, RefreshPriority.Low);
                        _logger.LogTrace("Queued Create refresh for library: {LibraryName} ({ItemId})", name, folder.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error queueing Create refresh for library '{LibraryName}'", name);
                    }
                }
            }
            */
            var refreshOptions = removeMode ? refreshOptionsRemove : createMode ? refreshOptionsCreate : refreshOptionsUpdate;
            var refreshOptionsMessage = removeMode ? "Remove" : createMode ? "Create" : "Update";
            foreach (var (name, folder) in validLibraryFolders)
            {
                try
                {
                    _providerManager.QueueRefresh(folder.Id, refreshOptions, RefreshPriority.Normal);
                    await folder.ValidateChildren(
                        new Progress<double>(),
                        refreshOptions,
                        recursive: true,
                        allowRemoveRoot: false,
                        cancellationToken: CancellationToken.None);
                    _logger.LogTrace("Queued {RefreshOptionsMessage} refresh for library: {LibraryName} ({ItemId})", refreshOptionsMessage, name, folder.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error queueing {RefreshOptionsMessage} refresh for library '{LibraryName}'", refreshOptionsMessage, name);
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
    /// Checks if the "Refresh Library" task is currently running.
    /// </summary>
    public bool IsTaskRefreshLibraryRunning()
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

        // get the State property
        var state = taskWorker.State;
        return state == TaskState.Running;
    }

    /// <summary>
    /// Waits for the "Refresh Library" task to complete, with a timeout.
    /// </summary>
    public async Task<bool> WaitForTaskRefreshLibrary()
    {

        // Delay start of task to allow it to begin processing
        await Task.Delay(1000);
        // Wait for it to complete by checking the state
        var timeout = TimeSpan.FromMinutes(Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes)));
        var endTime = DateTime.UtcNow + timeout;
        var taskFinished = false;
        while (DateTime.UtcNow < endTime)
        {
            if(!IsTaskRefreshLibraryRunning())
            {
                taskFinished = true;
                break;
            }
            _logger.LogTrace($"TaskRefreshLibrary is still running, waiting a second...");
            await Task.Delay(1000);
        }

        if (taskFinished == false)
        {
            _logger.LogWarning("Scan timed out after {Timeout} minutes", timeout.TotalMinutes);
        }
        return taskFinished;
    }

    /// <summary>
    /// Scans all Jellyfin libraries for first-time plugin initialization.
    /// Uses the same functionality as the "Scan All Libraries" button.
    /// </summary>
    public async Task<bool?> ScanAllLibraries(bool force=false)
    {
        // The marker has to sit inside a Jellyfin library folder, so create one per bridge folder: the
        // Library Directory itself until the libraries migration has run, every library folder after
        // (after the migration the root is no longer inside any library, so a marker there would be missed).
        var createdTempDirs = new List<string>();
        try
        {
            var manageJellyBridgeLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyBridgeLibrary));

            if (!force && !manageJellyBridgeLibrary)
            {
                _logger.LogDebug("Jellyseerr library management is disabled");
                return null;
            }

            if (force)
            {
                foreach (var bridgeDirectory in FolderUtils.GetBridgeDirectories())
                {
                    var tempDir = Path.Combine(bridgeDirectory, "_Recycle_JellyBridge");
                    if (!FolderUtils.FolderExistsThrowNull(tempDir)){
                        Directory.CreateDirectory(tempDir);
                    }
                    // Create temp file to force refresh
                    File.Create(Path.Combine(tempDir, ".ignore")).Close();
                    createdTempDirs.Add(tempDir);
                }
            }

            _logger.LogDebug("Starting full scan of all Jellyfin libraries...");

            // Wait for scan before and after running a refresh
            await WaitForTaskRefreshLibrary();
            ClearDotIgnoreCache();
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
            foreach (var tempDir in createdTempDirs)
            {
                // Clean up temp directories
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not remove the scan marker {TempDir}.", tempDir);
                }
            }
        }
    }

    /// <summary>
    /// Jellyfin 12 caches .ignore lookups per directory (DotIgnoreIgnoreRule). Library scans clear the DI singleton,
    /// but the library resolver uses separate rule instances (LibraryManager.EntityResolutionIgnoreRules) that are
    /// never cleared, so a new .ignore (e.g. after a request) is only honoured after a server restart. The rule class
    /// lives in Emby.Server.Implementations, which plugins can't reference, so it's found by name. Any failure
    /// (or an older Jellyfin without the cache) is a no-op.
    /// </summary>
    public void ClearDotIgnoreCache()
    {
        try
        {
            // 8b fix 2: resolve the rule instances ONCE and keep them. The ScanThenRefreshRunner fires this
            // in the background, potentially after the request scope it resolved from was disposed — getting
            // the DI rule again from that scope throws ObjectDisposedException, the clear is silently skipped
            // and the .ignore cache then goes stale without any sign. The rules live as long as Jellyfin's
            // LibraryManager, so the first resolution is good for every later call.
            // 8c-2: a resolve whose DI lookup hit a disposed scope is INCOMPLETE (the DI singleton rule is
            // missing) — use it once for this call but never cache it, so the next call resolves again and can
            // pick up the complete set. Only a complete (or genuinely empty) list is process-wide cacheable.
            List<object> rules;
            if (_dotIgnoreRules != null)
            {
                rules = _dotIgnoreRules;
            }
            else
            {
                rules = ResolveDotIgnoreRules(out var complete);
                if (complete)
                {
                    _dotIgnoreRules = rules;
                }
                else
                {
                    _logger.LogDebug("The .ignore rule list was incomplete (dead DI scope); using it once without caching — next clear resolves again");
                }
            }

            if (rules.Count == 0)
            {
                return; // rule class not found (older Jellyfin) — already logged once
            }

            foreach (var rule in rules)
            {
                _dotIgnoreClear!.Invoke(rule, null);
            }
        }
        catch (ObjectDisposedException ex)
        {
            // Safety net only: 8c fix 2 moved the scope-sensitive GetService inside ResolveDotIgnoreRules,
            // so a dead scope no longer reaches here. If anything ever does, the rules are resolved and
            // cached statically and the next call from any scope reuses them.
            _logger.LogDebug("Skipping the .ignore directory cache clear: the service scope was already disposed ({Message})", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clear Jellyfin .ignore directory cache");
        }
    }

    /// <summary>
    /// Finds the DotIgnoreIgnoreRule instances by reflection (the DI singleton and the library resolver's own)
    /// for <see cref="ClearDotIgnoreCache"/> to keep — see 8b fix 2. An empty list means the rule class was not
    /// found (older Jellyfin without the cache).
    /// </summary>
    private List<object> ResolveDotIgnoreRules(out bool complete)
    {
        complete = true; // 8c-2: false when the DI rule was unreachable, so the caller must not cache
        var ruleType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Emby.Server.Implementations.Library.DotIgnoreIgnoreRule", throwOnError: false))
            .FirstOrDefault(t => t != null);
        var clear = ruleType?.GetMethod("ClearDirectoryCache", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        if (ruleType == null || clear == null)
        {
            _logger.LogDebug("Jellyfin .ignore directory cache not found; skipping cache clear");
            return [];
        }
        _dotIgnoreClear = clear;

        // The library resolver uses its own rule instances (LibraryManager.EntityResolutionIgnoreRules),
        // separate from the DI singleton used by the library monitor - clear all of them.
        // 8c fix 2: only the DI lookup can throw ObjectDisposedException (request scope already disposed);
        // catch it HERE so the library manager's own rule instances — the ones the resolver really uses —
        // are still collected and cached even when the first call on an instance comes from a dead scope.
        var rules = new List<object>();
        try
        {
            var diRule = _serviceProvider.GetService(ruleType);
            if (diRule != null)
            {
                rules.Add(diRule);
            }
        }
        catch (ObjectDisposedException ex)
        {
            // 8c-2: the DI singleton is missing from this list — it may still clear this call's caches, but
            // caching it would freeze the incomplete set for the whole process.
            complete = false;
            _logger.LogDebug("The service scope was already disposed; collecting only the library manager's .ignore rule instances ({Message})", ex.Message);
        }
        var libraryManager = (object)_libraryManager.Inner;
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        for (var t = libraryManager.GetType(); t != null; t = t.BaseType)
        {
            // Only look at fields typed as a rule or an array of (interfaces of) rules, e.g. IResolverIgnoreRule[]
            foreach (var field in t.GetFields(flags))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsAssignableFrom(ruleType) && !fieldType.IsArray && fieldType != typeof(object))
                {
                    var value = field.GetValue(libraryManager);
                    if (value != null && ruleType.IsInstanceOfType(value))
                    {
                        rules.Add(value);
                    }
                }
                else if (fieldType.IsArray && fieldType.GetElementType() is Type elementType
                    && elementType != typeof(object) && elementType.IsAssignableFrom(ruleType)
                    && field.GetValue(libraryManager) is Array array)
                {
                    rules.AddRange(array.Cast<object?>().Where(o => o != null && ruleType.IsInstanceOfType(o))!);
                }
            }
        }
        var distinctRules = rules.Distinct().ToList();
        _logger.LogDebug("Resolved Jellyfin .ignore directory cache rules ({Count} rule instances)", distinctRules.Count);
        return distinctRules;
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

    /// <summary>
    /// 8b fix 1: a FRESH DirectoryService for every refresh-option build. A DirectoryService caches its
    /// GetFiles() results for the DirectoryService.RefreshCycle (65 seconds by default), so one long-lived
    /// instance (DI's default) makes a re-created folder invisible to the refresh of a title that existed
    /// before — Jellyfin sees no media and unregisters the item instead of adding it back. A new instance
    /// per refresh has an empty cache and reads the disk.
    /// </summary>
    private DirectoryService NewDirectoryService() => new(_fileSystem);

    /// <summary>
    /// Builds the Remove refresh options (search for missing metadata only — detects removed items).
    /// </summary>
    private MetadataRefreshOptions BuildRemoveRefreshOptions() => new(NewDirectoryService())
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

    /// <summary>
    /// Builds the Update refresh options (minimal refresh to reload user data like play counts).
    /// </summary>
    private MetadataRefreshOptions BuildUpdateRefreshOptions() => new(NewDirectoryService())
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

    /// <summary>
    /// Builds the Create refresh options (replace all metadata for created/updated items).
    /// </summary>
    /// <param name="refreshImages">Whether images are refreshed too.</param>
    private MetadataRefreshOptions BuildCreateRefreshOptions(bool refreshImages) => new(NewDirectoryService())
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

    /// <summary>
    /// Queues a targeted refresh of the ONE Jellyfin library that contains the given folder — the same thing
    /// the REST endpoint <c>POST /Items/&lt;libraryId&gt;/Refresh?Recursive=true</c> does for that folder, and at
    /// High priority. Unlike <see cref="RefreshBridgeLibrary"/> it touches no other library, and unlike the
    /// Scan All Libraries plan it is never skipped while Jellyfin's Scan Media Library task is already running —
    /// that skip is what left new libraries empty on the live server (task 7).
    /// </summary>
    /// <param name="libraryDirectory">The library folder to match against the Jellyfin libraries' paths.</param>
    /// <param name="createMode">Use the Create options (full refresh, replace all metadata).</param>
    /// <param name="removeMode">Prefer the Remove options (detect removed items).</param>
    /// <param name="refreshImages">Passed through to the Create options.</param>
    /// <returns>True when a refresh was queued for the library.</returns>
    public async Task<bool> QueueLibraryRefreshAsync(string libraryDirectory, bool createMode = true, bool removeMode = false, bool refreshImages = true)
    {
        if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ManageJellyBridgeLibrary)))
        {
            _logger.LogDebug("Jellyseerr library management is disabled; skipping the targeted refresh of {Directory}", libraryDirectory);
            return false;
        }

        try
        {
            var folder = await FindBridgeLibraryFolderAsync(libraryDirectory);
            if (folder == null)
            {
                _logger.LogWarning("Could not find the Jellyfin library on {Directory} for a targeted refresh.", libraryDirectory);
                return false;
            }

            var refreshOptions = removeMode ? BuildRemoveRefreshOptions() : createMode ? BuildCreateRefreshOptions(refreshImages) : BuildUpdateRefreshOptions();
            _providerManager.QueueRefresh(folder.Id, refreshOptions, RefreshPriority.High);
            _logger.LogInformation("Queued targeted refresh of Jellyfin library '{LibraryName}' ({ItemId}).", folder.Name, folder.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing the targeted refresh of the Jellyfin library on {Directory}", libraryDirectory);
            return false;
        }
    }

    /// <summary>
    /// Safety net (task 7): queue a targeted refresh for every Discover library Jellyfin shows with 0 items
    /// although its folder holds title folders. Those are libraries Jellyfin never scanned since the sync wrote
    /// to them — the usual reason is the library being created while the Scan Media Library task is already
    /// running, which skips it. Called after every applied refresh plan, so an interval sync heals such a library.
    /// </summary>
    private void RefreshEmptyBridgeLibraries()
    {
        try
        {
            var root = DiscoverLibraries.GetRootDirectory();
            var virtualFolders = _libraryManager.Inner.GetVirtualFolders();
            foreach (var library in DiscoverLibraries.GetAll())
            {
                try
                {
                    var directory = library.GetDirectory(root);
                    if (!Directory.Exists(directory) || !HasTitleFolders(directory))
                    {
                        continue;
                    }

                    var virtualFolder = virtualFolders.FirstOrDefault(folder =>
                        (folder.Locations ?? Array.Empty<string>()).Any(location => FolderUtils.ArePathsEqual(location, directory)));
                    if (virtualFolder == null || string.IsNullOrEmpty(virtualFolder.ItemId) || !Guid.TryParse(virtualFolder.ItemId, out var libraryItemId))
                    {
                        continue; // no Jellyfin library yet — Create queues the first refresh itself
                    }

                    var descendants = _libraryManager.Inner.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        AncestorIds = new[] { libraryItemId },
                        Recursive = true,
                        Limit = 1 // only "is there anything?" — never load the whole library
                    });
                    if (descendants.Count > 0)
                    {
                        continue;
                    }

                    _logger.LogInformation("Library '{LibraryName}' holds title folders but Jellyfin shows 0 items — queueing a targeted refresh.", library.Name);
                    _providerManager.QueueRefresh(libraryItemId, BuildCreateRefreshOptions(refreshImages: true), RefreshPriority.High);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "The empty-library safety net failed for library {Library}", library.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The empty-library safety net failed");
        }
    }

    /// <summary>
    /// True when the folder holds at least one title folder (a folder containing a metadata.json).
    /// </summary>
    private bool HasTitleFolders(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "metadata.json", SearchOption.AllDirectories).Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not count the title folders of {Directory}", directory);
            return false;
        }
    }

    /// <summary>
    /// Gets the CollectionFolder of the Jellyfin library whose paths contain the folder, retrying while the
    /// folder item still materializes after a fresh AddVirtualFolder (the ItemId can briefly be empty, 10.10).
    /// </summary>
    private async Task<MediaBrowser.Controller.Entities.Folder?> FindBridgeLibraryFolderAsync(string libraryDirectory)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var virtualFolder = _libraryManager.Inner.GetVirtualFolders().FirstOrDefault(folder =>
                (folder.Locations ?? Array.Empty<string>()).Any(location => FolderUtils.ArePathsEqual(location, libraryDirectory)));
            if (virtualFolder != null && Guid.TryParse(virtualFolder.ItemId, out var libraryItemId))
            {
                var folder = _libraryManager.Inner.GetItemById(libraryItemId) as MediaBrowser.Controller.Entities.Folder;
                if (folder != null)
                {
                    return folder;
                }
            }

            await Task.Delay(500);
        }

        return null;
    }
}
