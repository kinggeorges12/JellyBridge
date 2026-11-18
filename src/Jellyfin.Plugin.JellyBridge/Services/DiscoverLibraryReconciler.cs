using System.Globalization;
using System.Linq;
using System.IO;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// One reconcile step per library: compares the ACTUAL state (folder on disk, Jellyfin library, list settings,
/// order, LatestItemsExcludes, enabled flag) with the saved card and fixes what differs — "save applies the
/// change" (task 9). Work from the state, not from which field was clicked: every save, the Create endpoint, the
/// per-library Sync and the start of every full-sync pass call ApplyAsync, and each check is idempotent and cheap
/// when nothing differs (task 9 reviewer item 1/5/9).
/// - folder name = library name: move the folder (a rename arrives through renamedFrom; a disk-only rename heals back).
/// - Jellyfin library exists at all: created here — Create's body lives in this class, the endpoint is the wrapper.
/// - CollectionType = Type, or display name = library name: re-create the Jellyfin library (Jellyfin cannot
///   change it in place): remove the virtual folder (files kept) and AddVirtualFolder with the library's OWN
///   LibraryOptions (reviewer item 4), then swap the old id for the new one in every user's LatestItemsExcludes.
///   Refused while one of the library's items is in a /Sessions NowPlayingItem (reviewer item 4).
/// - titles = List/Networks/Pages/Minimum Rating/Votes/Type: one sync of that library when the stored
///   LastSyncedSignature differs (the sync's own cleanup removes titles that no longer fit; reviewer item 6).
/// - order = Order in Jellyfin: re-sort that library when LastSyncedOrder differs (reviewer item 7).
/// - on/off: off never syncs, on syncs now when the save turned it on (reviewer item 8).
/// Legacy libraries are never touched. Re-entry guard: the sync step calls SyncService.SyncLibraryCoreAsync, the
/// entry point that skips reconciliation (SyncLibraryAsync and the full-sync loop reconcile, the core never does).
/// The caller holds the plugin sync lock.
/// </summary>
public class DiscoverLibraryReconciler
{
    /// <summary>One library's reconcile report, shaped for the page's short sync message (task 9 item 3, reviewer item 10).</summary>
    public sealed class ReconcileResult
    {
        /// <summary>Gets or sets the library's name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether every step succeeded; one failed library never stops the others.</summary>
        public bool Ok { get; set; } = true;

        /// <summary>Gets the fixes this reconcile applied ("moved", "re-created", "created", "synced", "re-sorted", ...).</summary>
        public List<string> Steps { get; set; } = new();

        /// <summary>Gets or sets the message set when a step refused (e.g. playback active) or failed; the other libraries still reconcile.</summary>
        public string? Message { get; set; }

        /// <summary>Gets or sets the sync's own message, when this reconcile synced the library.</summary>
        public string? SyncMessage { get; set; }

        /// <summary>Gets or sets the library's title count on disk after the reconcile (the safety-net pass skips it).</summary>
        public int TitleCount { get; set; }

        /// <summary>Gets or sets the Jellyfin library's current ItemId — the new one after a re-create.</summary>
        public string? JellyfinLibraryId { get; set; }

        /// <summary>Gets or sets a value indicating whether the create path queued the Task 7 targeted refresh (reviewer 9a-6: the endpoint adds one only when this is false).</summary>
        public bool RefreshQueued { get; set; }
    }

    private readonly DebugLogger<DiscoverLibraryReconciler> _logger;

    /// <summary>
    /// Users whose LatestItemsExcludes this instance already verified or updated for a library id — the
    /// excludes bookkeeping then reads each <c>UserDto</c> at most once per id per request (reviewer 9a-8).
    /// </summary>
    private readonly HashSet<(Guid User, Guid Library)> _excludesChecked = new();
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIUserManager _userManager;
    private readonly SyncService _syncService;
    private readonly SortService _sortService;
    private readonly RefreshService _refreshService;
    private readonly ISessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoverLibraryReconciler"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="libraryManager">The Jellyfin library manager wrapper.</param>
    /// <param name="userManager">The Jellyfin user manager wrapper (LatestItemsExcludes bookkeeping).</param>
    /// <param name="syncService">The sync service — the reconcile sync step uses its no-re-entry core.</param>
    /// <param name="sortService">The sort service — the reconcile sort step.</param>
    /// <param name="refreshService">The refresh service — the Task 7 targeted refresh after a create.</param>
    /// <param name="sessionManager">The session manager — the playback guard for a re-create.</param>
    public DiscoverLibraryReconciler(
        ILoggerFactory loggerFactory,
        JellyfinILibraryManager libraryManager,
        JellyfinIUserManager userManager,
        SyncService syncService,
        SortService sortService,
        RefreshService refreshService,
        ISessionManager sessionManager)
    {
        _logger = new DebugLogger<DiscoverLibraryReconciler>(loggerFactory.CreateLogger<DiscoverLibraryReconciler>());
        _libraryManager = libraryManager;
        _userManager = userManager;
        _syncService = syncService;
        _sortService = sortService;
        _refreshService = refreshService;
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Reconciles one saved library against reality.
    /// </summary>
    /// <param name="library">The library as now stored in the config.</param>
    /// <param name="previous">The library as stored BEFORE this save (null for a new library or the safety net).</param>
    /// <param name="isNew">True when this library was not in the config before the save.</param>
    /// <param name="renamedFrom">The old name when this save renames the library (the page sends it transiently).</param>
    /// <param name="runSyncAndSort">False for the sync-path safety net: only structural fixes, no sync or sort.</param>
    public async Task<ReconcileResult> ApplyAsync(
        DiscoverLibraryConfig library,
        DiscoverLibraryConfig? previous,
        bool isNew,
        string? renamedFrom,
        bool runSyncAndSort = true)
    {
        var result = new ReconcileResult { Name = library.Name };
        if (library.Legacy)
        {
            // A Legacy library belongs to the migration; the reconciler never touches it (task 9 reviewer item 7).
            result.Steps.Add("legacy (untouched)");
            return result;
        }

        var root = DiscoverLibraries.GetRootDirectory();
        var directory = library.GetDirectory(root);
        // 9a-1: the folder a renamed save moves FROM. The Jellyfin library of a renaming library still carries
        // the OLD name and points at the OLD path, so the old directory is how its library is found below.
        var oldDirectory = RenameOldDirectory(root, renamedFrom);

        try
        {
            await ApplyFolderAsync(library, oldDirectory, root, directory, result);
            if (!result.Ok)
            {
                return result;
            }

            await ApplyJellyfinLibraryAsync(library, directory, oldDirectory, result);
            if (!result.Ok)
            {
                return result;
            }

            await ApplyExcludesAsync(result);

            if (runSyncAndSort)
            {
                var toggledOn = previous is { EnabledOrDefault: false } && library.EnabledOrDefault;
                // "created" counts as needing the follow-up sync too (9a-6): the Create endpoint wrapper passes
                // isNew=false for an already-stored library, and the reconciler itself is what decides whether a
                // Jellyfin library had to be made — a Create click on a filled library must not sync again.
                var needsSync = isNew
                    || toggledOn
                    || library.SyncSignatureChangedSinceLastSync
                    || result.Steps.Contains("created")
                    || result.Steps.Contains("re-created");

                if (!library.EnabledOrDefault)
                {
                    // Off: no sync, no sort, nothing deleted or re-created beyond the structural fixes above
                    // (reviewer item 8).
                    result.Steps.Add("off (no sync)");
                }
                else if (needsSync)
                {
                    // Titles row: the sync's list-change cleanup (which runs on the stored signature differing,
                    // not only the list type) removes titles that no longer fit, and AsList ordering is stamped
                    // inside the sync. 9a-6: for a created or re-created library the sync skips its own refresh;
                    // the one create-mode refresh is queued below, after the titles are on disk.
                    var syncResult = await _syncService.SyncLibraryCoreAsync(library, skipRefreshQueue: JustCreated(result));
                    result.SyncMessage = syncResult.Message;
                    result.Ok &= syncResult.Success;
                    result.Steps.Add("synced");
                }

                // Order row: re-sort when the order differs from the one last recorded — except AsList after a
                // sync, which already stamped the list-order play counts. 9a-5: LastSyncedOrder is null on every
                // library right after the upgrade; that alone must never re-sort (a Jellyfin-order library would
                // get zeros stamped). Without a recorded order, diff against the order of the entry stored before
                // this save and record the current order silently when it matches.
                if (library.EnabledOrDefault && library.LastSyncedOrder != library.OrderOrDefault)
                {
                    var orderActuallyChanged = library.LastSyncedOrder != null
                        || previous?.OrderOrDefault != library.OrderOrDefault;
                    if (orderActuallyChanged && !(needsSync && library.OrderOrDefault == DiscoverLibraryOrder.AsList))
                    {
                        var sortResult = await _sortService.SortLibraryAsync(library);
                        result.Ok &= sortResult.Success;
                        result.Steps.Add("re-sorted");
                    }

                    library.LastSyncedOrder = library.OrderOrDefault;
                    Plugin.Instance?.SaveConfiguration();
                }
            }

            // Task 7 + 9a-6: exactly one targeted create-mode refresh for a library whose Jellyfin library was just
            // made, after the sync above wrote its titles (or right away when no sync runs: disabled card, or the
            // sync-start safety net whose sync follows and whose folder already holds the titles).
            if (JustCreated(result) && !result.RefreshQueued)
            {
                _refreshService.ClearDotIgnoreCache();
                result.RefreshQueued = await _refreshService.QueueLibraryRefreshAsync(directory, createMode: true, removeMode: false, refreshImages: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconcile of library {Library} failed", library.Name);
            result.Ok = false;
            result.Message ??= ex.Message;
        }

        result.TitleCount = runSyncAndSort ? CountTitlesOnDisk(directory) : 0;
        return result;
    }

    /// <summary>True when this reconcile created or re-created the library's Jellyfin library.</summary>
    private static bool JustCreated(ReconcileResult result) =>
        result.Steps.Contains("created") || result.Steps.Contains("re-created");

    /// <summary>The directory a renaming save moves FROM (the folder of the old name), or null without a renamedFrom.</summary>
    private static string? RenameOldDirectory(string root, string? renamedFrom) =>
        string.IsNullOrWhiteSpace(renamedFrom)
            ? null
            : FolderUtils.GetNormalizedPath(Path.Combine(root, FolderUtils.SanitizeFileName(renamedFrom.Trim())))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Folder name = library name: for a save-rename, move the folder old → new; for a disk-only rename (heal),
    /// move the found old folder back to the name the card says.
    /// </summary>
    private async Task ApplyFolderAsync(DiscoverLibraryConfig library, string? oldDirectory, string root, string directory, ReconcileResult result)
    {
        if (oldDirectory != null && !FolderUtils.ArePathsEqual(oldDirectory, directory) && Directory.Exists(oldDirectory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.Move(oldDirectory, directory);
                result.Steps.Add("moved");
                _logger.LogInformation("Reconcile: moved library folder {Old} -> {New}.", oldDirectory, directory);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = $"Could not move the library folder: {ex.Message}";
                return;
            }
        }

        // Heal a folder renamed on disk behind our back (task 9 test row "heal at sync start"): the card's
        // folder is gone and the virtual folder's locations are all stale too. A candidate is a directory under
        // the bridge root that no card and no virtual folder claims AND that provably holds titles (a
        // metadata.json inside) — 9a-4: an unclaimed leftover of a deleted-with-files-kept library must never be
        // adopted, so nothing moves when the folder cannot be proven, and two provable candidates refuse rather
        // than guess.
        var virtualFolders = _libraryManager.Inner.GetVirtualFolders();
        var jellyfinLibrary = FindVirtualFolder(virtualFolders, directory)
            ?? FindVirtualFolderByName(virtualFolders, library.Name);
        _logger.LogDebug("Reconcile folder check {Library}: dir={Directory} exists={Exists}, Jellyfin library={Vf}",
            library.Name, directory, Directory.Exists(directory), jellyfinLibrary?.Name ?? "none");
        // Adoption needs the library's own Jellyfin library still pointing at the (now missing) folder: without it
        // a brand-new card could adopt a stranger's leftover folder. The full sync reconciles every library BEFORE
        // LibraryLoop creates the per-library folders (SyncService.SyncFromJellyseerr), so this sees the disk as is.
        if (!Directory.Exists(directory) && jellyfinLibrary != null)
        {
            var existing = (jellyfinLibrary.Locations ?? Array.Empty<string>()).FirstOrDefault(location => Directory.Exists(location));
            if (existing == null)
            {
                var claimed = new HashSet<string>(
                    virtualFolders.SelectMany(folder => folder.Locations ?? Array.Empty<string>()),
                    StringComparer.OrdinalIgnoreCase);
                var orphans = Directory.Exists(root)
                    ? Directory.GetDirectories(root)
                        .Where(candidate => !FolderUtils.ArePathsEqual(candidate, directory)
                            && !claimed.Contains(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                            && !DiscoverLibraries.GetAll().Any(other => other != library
                                && FolderUtils.ArePathsEqual(other.GetDirectory(root), candidate)))
                        .ToList()
                    : new List<string>();
                var provable = orphans.Where(orphan => CountTitlesOnDisk(orphan) > 0).ToList();
                _logger.LogDebug("Reconcile orphan scan for {Library}: orphans=[{Orphans}] provable=[{Provable}]",
                    library.Name, string.Join(", ", orphans.Select(Path.GetFileName)), string.Join(", ", provable.Select(Path.GetFileName)));
                if (provable.Count == 1)
                {
                    existing = provable[0];
                    _logger.LogWarning("Reconcile: healing library '{Library}' — adopting the unclaimed folder {Orphan} ({Count} titles on disk) as {Target}.",
                        library.Name, existing, CountTitlesOnDisk(existing), directory);
                }
                else if (provable.Count > 1)
                {
                    result.Ok = false;
                    result.Message = $"Could not heal '{library.Name}': {provable.Count} unclaimed folders under the Library Directory could belong to it ({string.Join(", ", provable.Select(p => Path.GetFileName(p)))}) — refusing to guess.";
                    _logger.LogWarning("{Message}", result.Message);
                    return;
                }
                else if (orphans.Count > 0)
                {
                    _logger.LogWarning("Reconcile: not healing library '{Library}': the unclaimed folder(s) under {Root} hold no titles, so none provably belongs to it — leaving them alone.",
                        library.Name, root);
                }
            }

            if (existing != null && !FolderUtils.ArePathsEqual(existing, directory)
                && existing.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Move(existing, directory);
                    result.Steps.Add("moved");
                    _logger.LogInformation("Reconcile: healed library folder {Old} -> {New}.", existing, directory);
                }
                catch (Exception ex)
                {
                    result.Ok = false;
                    result.Message = $"Could not heal the library folder: {ex.Message}";
                }
            }
        }

        if (!Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
                var seedFile = Path.Combine(directory, ".seed");
                if (!File.Exists(seedFile))
                {
                    await File.WriteAllTextAsync(seedFile, "JellyBridge library folder — the seed keeps Jellyfin from skipping it while it is empty.\n");
                }

                result.Steps.Add("folder");
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = $"Could not create the folder {directory}: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Jellyfin library exists / display name / CollectionType: create when missing, re-create (files kept) when
    /// the name or the type no longer matches the card. A path-only difference repoints in place (keeps the id).
    /// A renaming library's Jellyfin library is found by the OLD directory / name first (9a-1): after the folder
    /// move nothing matches the new path or name, and without this the old (dead-path) library would be left
    /// orphaned while a second one got created.
    /// </summary>
    private async Task ApplyJellyfinLibraryAsync(DiscoverLibraryConfig library, string directory, string? oldDirectory, ReconcileResult result)
    {
        var virtualFolders = _libraryManager.Inner.GetVirtualFolders();
        var jellyfinLibrary = FindVirtualFolder(virtualFolders, directory)
            ?? (oldDirectory == null ? null : FindVirtualFolder(virtualFolders, oldDirectory))
            ?? FindVirtualFolderByName(virtualFolders, library.Name)
            ?? (oldDirectory == null ? null : FindVirtualFolderByName(virtualFolders, Path.GetFileName(oldDirectory)))
            ?? FindVirtualFolderByStaleBridgePath(virtualFolders, directory);

        if (jellyfinLibrary == null)
        {
            await CreateJellyfinLibraryAsync(library, directory, ownOptions: null, result);
            return;
        }

        var nameMatches = string.Equals(jellyfinLibrary.Name, library.Name, StringComparison.OrdinalIgnoreCase);
        var typeMatches = CollectionTypeMatches(jellyfinLibrary, library.LibraryType);
        var pathMatches = (jellyfinLibrary.Locations ?? Array.Empty<string>()).Any(location => FolderUtils.ArePathsEqual(location, directory));

        if (nameMatches && typeMatches && !pathMatches)
        {
            // Only the path moved: repoint without re-creating, so the library id and watch marks survive.
            var locations = (jellyfinLibrary.Locations ?? Array.Empty<string>()).ToList();
            try
            {
                if (!locations.Any(location => FolderUtils.ArePathsEqual(location, directory)))
                {
                    _libraryManager.Inner.AddMediaPath(jellyfinLibrary.Name, new MediaPathInfo { Path = directory });
                }

                foreach (var stale in locations.Where(location => !FolderUtils.ArePathsEqual(location, directory)))
                {
                    _libraryManager.Inner.RemoveMediaPath(jellyfinLibrary.Name, stale);
                }

                result.Steps.Add("repointed");
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = $"Repointing Jellyfin library '{jellyfinLibrary.Name}' failed: {ex.Message}";
            }

            return;
        }

        if (!nameMatches || !typeMatches)
        {
            // Re-create (task 9 decision): rename AND type change remove the virtual folder (files stay on disk) and
            // AddVirtualFolder with the library's own options. Warn the page-side user: placeholder watch marks reset.
            if (!library.EnabledOrDefault)
            {
                // Reviewer item 8: an off library must never be deleted or re-created — the change heals when it
                // is turned on again.
                result.Steps.Add("re-create deferred (library off)");
                result.JellyfinLibraryId = jellyfinLibrary.ItemId;
                return;
            }

            if (IsLibraryInPlayback(directory))
            {
                // Reviewer item 4: never re-create while something of this library is playing; leave it to heal later.
                result.Ok = false;
                result.Message = $"{library.Name}: something in this library is playing right now; the change applies on the next save or sync.";
                result.Steps.Add("refused (playing)");
                return;
            }

            var ownOptions = ReadOwnLibraryOptions(jellyfinLibrary.ItemId);
            var oldItemId = jellyfinLibrary.ItemId;
            try
            {
                await _libraryManager.Inner.RemoveVirtualFolder(jellyfinLibrary.Name, refreshLibrary: false);
                // Drop the removed collection folder from Jellyfin's root right away (what refreshLibrary: true does,
                // without its full library scan). Without it 10.10's AddVirtualFolder below refreshed the root, hit
                // the deleted /config/root/default/<old name> and threw, leaving the renamed library uncreated.
                await _libraryManager.Inner.ValidateTopLibraryFolders(CancellationToken.None, true);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = $"Could not remove Jellyfin library '{jellyfinLibrary.Name}': {ex.Message}";
                return;
            }

            await CreateJellyfinLibraryAsync(library, directory, ownOptions, result);
            if (result.Ok && result.JellyfinLibraryId != null)
            {
                await RemoveFromLatestItemsExcludesAsync(oldItemId);
                result.Steps.Remove("created");
                result.Steps.Add("re-created");
            }
        }
        else
        {
            // Nothing structural to fix: the existing library's id feeds the excludes check below.
            result.JellyfinLibraryId = jellyfinLibrary.ItemId;
        }
    }

    private async Task CreateJellyfinLibraryAsync(DiscoverLibraryConfig library, string directory, LibraryOptions? ownOptions, ReconcileResult result)
    {
        var options = ownOptions ?? DefaultLibraryOptions();
        options.PathInfos = new[] { new MediaPathInfo { Path = directory } };
        var collectionType = library.LibraryType switch
        {
            DiscoverLibraryType.Movies => CollectionTypeOptions.movies,
            DiscoverLibraryType.Series => CollectionTypeOptions.tvshows,
            _ => (CollectionTypeOptions?)null
        };

        try
        {
            // refreshLibrary: true on purpose (10.10 materializes the CollectionFolder — and its ItemId — only
            // after a scan; see the 10.10 finding in the 5a progress-log row).
            await _libraryManager.Inner.AddVirtualFolder(library.Name, collectionType, options, refreshLibrary: true);
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Message = $"Could not create the Jellyfin library '{library.Name}': {ex.Message}";
            return;
        }

        var created = await FindVirtualFolderWithIdAsync(directory);
        result.JellyfinLibraryId = created?.ItemId;
        result.Steps.Add("created");

        // Task 7's targeted refresh is queued by ApplyAsync AFTER the follow-up sync has written the titles:
        // queued here it ran on the still-empty folder and the new library stayed empty (libraries-api-test task 7).
        _logger.LogInformation("Reconcile: created Jellyfin library '{Name}' on {Path}.", library.Name, directory);
    }

    private async Task ApplyExcludesAsync(ReconcileResult result)
    {
        if (!string.IsNullOrEmpty(result.JellyfinLibraryId))
        {
            await AddToLatestItemsExcludesAsync(result.JellyfinLibraryId);
        }
    }

    /// <summary>True while any active playback session's NowPlayingItem lives under this library's folder.</summary>
    private bool IsLibraryInPlayback(string directory)
    {
        try
        {
            // 9a-2: ISessionManager.Sessions is the plain property (get_Sessions exists on all three supported
            // Jellyfin versions — no reflection). Compare against directory + separator so a library named
            // "Pop" never matches a playing item of "Pop 2".
            var prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var session in _sessionManager.Sessions)
            {
                var path = session?.NowPlayingItem?.Path;
                if (!string.IsNullOrEmpty(path)
                    && (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || path.Equals(directory, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playback check for a library re-create failed; proceeding with the check as 'not playing'");
        }

        return false;
    }

    /// <summary>The library's OWN LibraryOptions (reviewer item 4: a retype must not reset them to the Default's).</summary>
    private LibraryOptions? ReadOwnLibraryOptions(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId) || !Guid.TryParse(itemId, out var libraryId))
        {
            return null;
        }

        try
        {
            var folder = _libraryManager.Inner.GetItemById(libraryId);
            var options = folder == null ? null : _libraryManager.Inner.GetLibraryOptions(folder);
            if (options == null)
            {
                return null;
            }

            // Round-trip copy so replacing PathInfos never mutates the live options object.
            return System.Text.Json.JsonSerializer.Deserialize<LibraryOptions>(
                System.Text.Json.JsonSerializer.Serialize(options));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the own LibraryOptions of library {ItemId}; falling back to the Default's", itemId);
            return null;
        }
    }

    private static bool CollectionTypeMatches(VirtualFolderInfo jellyfinLibrary, DiscoverLibraryType libraryType) => libraryType switch
    {
        // "All" means no type constraint: Jellyfin stores an untyped folder as null (or mixed).
        DiscoverLibraryType.All => jellyfinLibrary.CollectionType == null || jellyfinLibrary.CollectionType == CollectionTypeOptions.mixed,
        DiscoverLibraryType.Movies => jellyfinLibrary.CollectionType == CollectionTypeOptions.movies,
        DiscoverLibraryType.Series => jellyfinLibrary.CollectionType == CollectionTypeOptions.tvshows,
        _ => true
    };

    private static VirtualFolderInfo? FindVirtualFolder(IEnumerable<VirtualFolderInfo> virtualFolders, string directory) =>
        virtualFolders.FirstOrDefault(folder => (folder.Locations ?? Array.Empty<string>()).Any(location => FolderUtils.ArePathsEqual(location, directory)));

    private static VirtualFolderInfo? FindVirtualFolderByName(IEnumerable<VirtualFolderInfo> virtualFolders, string name) =>
        virtualFolders.FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A virtual folder that points at a now-gone path under the bridge root that no other library claims —
    /// this is the library itself after a rename moved (or the disk heal moved) its folder out from under it.
    /// Only consulted when nothing matches the card's directory or name.
    /// </summary>
    private static VirtualFolderInfo? FindVirtualFolderByStaleBridgePath(IEnumerable<VirtualFolderInfo> virtualFolders, string directory)
    {
        var root = DiscoverLibraries.GetRootDirectory();
        return virtualFolders.FirstOrDefault(folder =>
            (folder.Locations ?? Array.Empty<string>()).Any(location =>
                !Directory.Exists(location)
                && location.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<VirtualFolderInfo?> FindVirtualFolderWithIdAsync(string directory)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var folder = FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), directory);
            if (folder != null && !string.IsNullOrEmpty(folder.ItemId))
            {
                return folder;
            }

            await Task.Delay(500);
        }

        _logger.LogWarning("The Jellyfin library on {Directory} still has no id after 20s.", directory);
        return FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), directory);
    }

    /// <summary>
    /// The LibraryOptions for a new Jellyfin library: the Default library's options (round-trip copy, the caller
    /// replaces PathInfos), or a quiet default when the Default library has no Jellyfin library yet.
    /// </summary>
    private LibraryOptions DefaultLibraryOptions()
    {
        var defaultLibrary = DiscoverLibraries.GetDefault();
        var defaultInfo = defaultLibrary == null
            ? null
            : FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), defaultLibrary.GetDirectory(DiscoverLibraries.GetRootDirectory()));
        if (defaultInfo != null && Guid.TryParse(defaultInfo.ItemId, out var defaultId))
        {
            var own = ReadOwnLibraryOptions(defaultId.ToString("N"));
            if (own != null)
            {
                return own;
            }
        }

        return new LibraryOptions { EnableRealtimeMonitor = false };
    }

    /// <summary>
    /// Counts a library's titles on disk: the folders under it that hold a metadata.json (season folders
    /// inside a show hold none). 0 when the folder doesn't exist yet.
    /// </summary>
    public int CountTitlesOnDisk(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "metadata.json", SearchOption.AllDirectories).Length
                : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not count the titles of library folder {Directory}", directory);
            return 0;
        }
    }

    /// <summary>
    /// Adds a library's id to every user's LatestItemsExcludes, the same as the live server does for the
    /// Discover/Trending libraries — otherwise the new library's placeholders show up in Continue Watching.
    /// </summary>
    public async Task AddToLatestItemsExcludesAsync(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId) || !Guid.TryParse(itemId, out var libraryId))
        {
            _logger.LogWarning("Could not add the new library to the users' LatestItemsExcludes: no library id.");
            return;
        }

#if !JELLYFIN_10_11_3
        var users = _userManager.Inner.Users;
#else
        var users = _userManager.Inner.GetUsers();
#endif
        foreach (var user in users)
        {
            try
            {
                // 9a-8: within one reconcile each user's excludes are read at most once per library id —
                // the sync-start safety net calls this for every library on every pass.
                if (!_excludesChecked.Add((user.Id, libraryId)))
                {
                    continue;
                }

                var configuration = _userManager.Inner.GetUserDto(user).Configuration ?? new UserConfiguration();
                var latestItemsExcludes = new List<Guid>(configuration.LatestItemsExcludes ?? Array.Empty<Guid>());
                if (latestItemsExcludes.Contains(libraryId))
                {
                    continue;
                }

                latestItemsExcludes.Add(libraryId);
                configuration.LatestItemsExcludes = latestItemsExcludes.ToArray();
                await _userManager.Inner.UpdateConfigurationAsync(user.Id, configuration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not add library {LibraryId} to the LatestItemsExcludes of user {User}", libraryId, user.Username);
            }
        }
    }

    /// <summary>
    /// Best-effort counterpart of <see cref="AddToLatestItemsExcludesAsync"/> for a re-created or deleted library:
    /// remove its id from every user's LatestItemsExcludes. A stale id is harmless, so per-user failures are logged.
    /// </summary>
    public async Task RemoveFromLatestItemsExcludesAsync(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId) || !Guid.TryParse(itemId, out var libraryId))
        {
            return;
        }

#if !JELLYFIN_10_11_3
        var users = _userManager.Inner.Users;
#else
        var users = _userManager.Inner.GetUsers();
#endif
        foreach (var user in users)
        {
            try
            {
                var configuration = _userManager.Inner.GetUserDto(user).Configuration ?? new UserConfiguration();
                var latestItemsExcludes = new List<Guid>(configuration.LatestItemsExcludes ?? Array.Empty<Guid>());
                if (!latestItemsExcludes.Remove(libraryId))
                {
                    continue;
                }

                configuration.LatestItemsExcludes = latestItemsExcludes.ToArray();
                await _userManager.Inner.UpdateConfigurationAsync(user.Id, configuration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove library {LibraryId} from the LatestItemsExcludes of user {User}", libraryId, user.Username);
            }
        }
    }

}
