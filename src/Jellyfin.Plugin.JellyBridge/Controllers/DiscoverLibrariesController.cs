using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    /// <summary>
    /// The Discover libraries endpoints (task 5a): read the library list with each library's folder, Jellyfin
    /// library and title count; save the whole list with validation; and create, sync, rename or delete a single
    /// library. Every write runs under <see cref="Plugin.ExecuteWithLockAsync"/> and refuses (409) while the
    /// libraries migration is pending or running — nothing may be edited before the folders are moved.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("JellyBridge")]
    public class DiscoverLibrariesController : ControllerBase
    {
        private readonly DebugLogger<DiscoverLibrariesController> _logger;
        private readonly SyncService _syncService;
        private readonly RefreshService _refreshService;
        private readonly JellyfinILibraryManager _libraryManager;
        private readonly JellyfinIUserManager _userManager;
        private readonly DiscoverLibraryReconciler _reconciler;

        public DiscoverLibrariesController(
            ILoggerFactory loggerFactory,
            SyncService syncService,
            RefreshService refreshService,
            JellyfinILibraryManager libraryManager,
            JellyfinIUserManager userManager,
            DiscoverLibraryReconciler reconciler)
        {
            _logger = new DebugLogger<DiscoverLibrariesController>(loggerFactory.CreateLogger<DiscoverLibrariesController>());
            _syncService = syncService;
            _refreshService = refreshService;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _reconciler = reconciler;
        }

        /// <summary>
        /// Get every configured Discover library with its folder path, the Jellyfin library id and name when one
        /// points at that folder, and its title count on disk (folders holding a metadata.json). Wrapped with the
        /// <c>lastSync</c> field (<see cref="SyncService.LastSyncCompleted"/> in UTC, 5c review fix 6) so the
        /// settings page's status strip can show endpoint-initiated syncs, not only the scheduled task's, and with
        /// <c>isSyncRunning</c> (task 7) so the page can say "Waiting for the running sync to finish…" while a
        /// write request sits on the sync lock — the write endpoints don't report queue state.
        /// </summary>
        [HttpGet("Libraries")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<object> GetLibraries()
        {
            return Ok(new
            {
                lastSync = SyncService.LastSyncCompleted?.ToString("O"),
                isSyncRunning = Plugin.IsOperationRunning,
                libraries = GetLibrariesValue()
            });
        }

        /// <summary>
        /// Save the whole library list, then — unless <c>?apply=false</c> — reconcile every library against
        /// reality: "save applies the change" (task 9). One POST does what earlier needed separate Create,
        /// Rename and core VirtualFolders/Name calls: folder moves, Jellyfin library create/re-create (display
        /// name and type), the title sync when the title rules changed, the re-sort when the order changed, and
        /// the LatestItemsExcludes bookkeeping. Validation (each failure → 400 with a message) runs BEFORE any
        /// side effect: at least one library, names unique ignoring case, exactly one Default, Default not
        /// Trending, Trending libraries without networks, usable folder names that don't collide. Names are the
        /// identity: a POST that drops a stored library is rejected — removals go through Delete; a rename rides
        /// along as the transient renamedFrom of the renamed entry (task 9 reviewer item 2). The Default library
        /// is always saved enabled (decision 7). The response carries the per-library reconcile report
        /// (<c>results</c>, reviewer item 10) so the page can print one short line per library.
        /// </summary>
        /// <param name="libraries">The complete new list.</param>
        /// <param name="apply">False stores only — no reconcile (task 9 reviewer item 11, for the harness).</param>
        [HttpPost("Libraries")]
        public async Task<IActionResult> SaveLibraries([FromBody] List<DiscoverLibraryConfig>? libraries, [FromQuery] bool apply = true)
        {
            return await ExecuteWriteAsync("Save Discover Libraries", async () =>
            {
                var error = ValidateLibraryList(libraries);
                if (error != null)
                {
                    return BadRequest(new { success = false, message = error });
                }

                var previousByName = new Dictionary<string, DiscoverLibraryConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var previous in Plugin.GetConfiguration().DiscoverLibraries ?? new List<DiscoverLibraryConfig>())
                {
                    previousByName[previous.Name] = previous;
                }

                // Names are the library's identity: a stored name missing from the posted list (nor covered by a
                // renamedFrom) means the client deleted a library, which would orphan its folder and Jellyfin
                // library. A rename is a dropped name PLUS the renamedFrom that says so; new names are fine.
                var postedNames = new HashSet<string>(libraries!.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var library in libraries!)
                {
                    if (!string.IsNullOrWhiteSpace(library.RenamedFrom))
                    {
                        postedNames.Add(library.RenamedFrom!.Trim());
                    }
                }

                var droppedName = previousByName.Keys.FirstOrDefault(n => !postedNames.Contains(n));
                if (droppedName != null)
                {
                    return BadRequest(new { success = false, message = $"Use Delete to remove '{droppedName}' and Rename to rename it." });
                }

                // 9a-3: renamedFrom validation — BEFORE any side effect, like every other 400. A bad renamedFrom
                // could otherwise make the move step move ANOTHER library's folder: it must name a stored
                // library, must not also be a name the POST keeps, must be used by at most one entry, and the
                // new name must not already belong to another stored library. A Legacy entry ignores it (the
                // reconciler never touches a Legacy library).
                var keptNames = new HashSet<string>(libraries!.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                var renameSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var library in libraries!)
                {
                    if (library.Legacy || string.IsNullOrWhiteSpace(library.RenamedFrom))
                    {
                        continue;
                    }

                    var from = library.RenamedFrom!.Trim();
                    var carriedRetry = false;
                    if (string.Equals(from, library.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // a no-op rename
                    }

                    if (!previousByName.ContainsKey(from))
                    {
                        // 9a-b finding 3: a RETRY of a rename a previous save already stored but could not
                        // apply (refused while playing, or it failed) — the store carries the NEW name, and the
                        // old name lives in that entry's PendingRenameFrom, so renamedFrom may name that too.
                        carriedRetry = previousByName.TryGetValue(library.Name, out var carriedEntry)
                            && string.Equals(carriedEntry.PendingRenameFrom?.Trim(), from, StringComparison.OrdinalIgnoreCase);
                        if (!carriedRetry)
                        {
                            return BadRequest(new { success = false, message = $"renamedFrom '{from}' does not name a stored library." });
                        }
                    }

                    if (keptNames.Contains(from))
                    {
                        return BadRequest(new { success = false, message = $"renamedFrom '{from}' names a library the POST keeps by that name — a rename drops the old name." });
                    }

                    if (!renameSources.Add(from))
                    {
                        return BadRequest(new { success = false, message = $"renamedFrom '{from}' is used by more than one library." });
                    }

                    if (!carriedRetry
                        && previousByName.TryGetValue(library.Name, out var targetOwner)
                        && !string.Equals(targetOwner.Name, from, StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { success = false, message = $"'{library.Name}' already names the stored library '{targetOwner.Name}'; cannot be reached by renaming '{from}'." });
                    }
                }

                // 9a-b finding 1a: a NEW or renamed card must not take a name a Jellyfin library OUTSIDE the
                // bridge root already owns — the reconciler would block on it forever, so fail the 400 here
                // like every other check, before any side effect. Stored names not being renamed are not
                // checked: they already exist, and the reconciler's own gate protects them.
                foreach (var library in libraries!)
                {
                    if (library.Legacy)
                    {
                        continue;
                    }

                    var isNewOrRenamed = !previousByName.ContainsKey(library.Name)
                        || (!string.IsNullOrWhiteSpace(library.RenamedFrom)
                            && !string.Equals(library.RenamedFrom.Trim(), library.Name, StringComparison.OrdinalIgnoreCase));
                    if (isNewOrRenamed && FindForeignNameClash(library.Name) is string clashMessage)
                    {
                        return BadRequest(new { success = false, message = clashMessage });
                    }
                }

                // Decision 7: the Default library is always on — force it, whatever the client posted.
                libraries!.First(l => l.DefaultOrDefault).IsEnabled = true;

                foreach (var library in libraries!)
                {
                    // The entry this POST refers to: a renamed entry carries the stored name in renamedFrom.
                    var key = !string.IsNullOrWhiteSpace(library.RenamedFrom) ? library.RenamedFrom!.Trim() : library.Name;
                    previousByName.TryGetValue(key, out var previous);

                    // The editor round-trips the libraries it read, the sync bookkeeping included; when a client
                    // leaves it out of a library that existed before, keep the stored value so decision 9's and
                    // task 9's "changed since the last sync" detection survives the save.
                    if (previous != null)
                    {
                        library.LastSyncedListType ??= previous.LastSyncedListType;
                        library.LastSyncedSignature ??= previous.LastSyncedSignature;
                        library.LastSyncedOrder ??= previous.LastSyncedOrder;
                    }
                }

                // renamedFrom is transient (task 9 reviewer item 2): capture it for the reconcile below, then
                // clear it so it never reaches the saved config. The validated rename ALSO persists as
                // PendingRenameFrom (9a-b finding 3): if the reconcile below is refused (something is playing)
                // or fails, the next sync's safety-net reconcile retries the move + re-create. The value is
                // never taken from the client: either this POST's validated renamedFrom, or what the stored
                // entry already carried.
                var renamedFromByName = libraries!
                    .Where(l => !string.IsNullOrWhiteSpace(l.RenamedFrom))
                    .ToDictionary(l => l.Name, l => l.RenamedFrom!.Trim(), StringComparer.OrdinalIgnoreCase);
                foreach (var library in libraries!)
                {
                    var key = !string.IsNullOrWhiteSpace(library.RenamedFrom) ? library.RenamedFrom!.Trim() : library.Name;
                    previousByName.TryGetValue(key, out var previousEntry);
                    library.PendingRenameFrom = library.Legacy ? null
                        : renamedFromByName.TryGetValue(library.Name, out var pendingFrom)
                            && !string.Equals(pendingFrom, library.Name, StringComparison.OrdinalIgnoreCase)
                            ? pendingFrom
                            : previousEntry?.PendingRenameFrom;
                    library.RenamedFrom = null;
                }

                var config = Plugin.GetConfiguration();
                config.DiscoverLibraries = libraries!;
                Plugin.Instance?.SaveConfiguration();
                _logger.LogInformation("Discover libraries saved: {Libraries}",
                    string.Join(", ", libraries!.Select(l => $"{l.Name} [{l.ListType}{(l.DefaultOrDefault ? ", Default" : string.Empty)}]")));

                // Task 9: save applies the change — one reconcile per library, inside this same write lock (a
                // sync can take minutes, as Create always did; the page keeps its wait notice). A failed step
                // fails only that library's report (reviewer item 12's "other libraries still reconcile").
                var results = new List<DiscoverLibraryReconciler.ReconcileResult>();
                if (apply)
                {
                    foreach (var library in libraries!)
                    {
                        var oldName = renamedFromByName.TryGetValue(library.Name, out var from) ? from : null;
                        var previous = oldName != null && previousByName.TryGetValue(oldName, out var byOld)
                            ? byOld
                            : previousByName.TryGetValue(library.Name, out var byName) ? byName : null;
                        results.Add(await _reconciler.ApplyAsync(library, previous, isNew: previous == null, renamedFrom: oldName));
                    }
                }

                return Ok(new
                {
                    success = results.All(r => r.Ok),
                    message = $"Saved {libraries.Count} Discover libraries.",
                    libraries = GetLibrariesValue(),
                    results = results.Select(r => new
                    {
                        name = r.Name,
                        ok = r.Ok,
                        steps = r.Steps,
                        syncMessage = r.SyncMessage,
                        titleCount = r.TitleCount,
                        message = r.Message,
                        jellyfinLibraryId = r.JellyfinLibraryId
                    })
                });
            });
        }

        /// <summary>
        /// Create a library (task 9 reviewer item 5): a thin wrapper over the reconciler, which creates the
        /// folder (seeded, because Jellyfin skips empty folders) and the Jellyfin library with the Default
        /// library's options, adds the id to every user's LatestItemsExcludes, and then fills the library now
        /// (task 7): one sync of just this library plus a targeted refresh of its CollectionFolder, so titles
        /// appear without waiting for the next interval sync or a library scan. Kept for the Add button and
        /// the "Filling…" tag; POST Libraries now applies the same reconcile itself.
        /// </summary>
        /// <param name="name">The library name, matched ignoring case.</param>
        [HttpPost("Libraries/{name}/Create")]
        public async Task<IActionResult> CreateLibrary(string name)
        {
            return await ExecuteWriteAsync("Create Discover Library", async () =>
            {
                var library = FindLibrary(name);
                if (library == null)
                {
                    return NotFound(new { success = false, message = $"No Discover library named '{name}'." });
                }

                // 9a-6: the reconciler creates folder + Jellyfin library, adds the id to the users'
                // LatestItemsExcludes, and — when it had to CREATE the Jellyfin library — syncs the library and
                // queues the targeted refresh itself. isNew stays false on purpose: only a library whose Jellyfin
                // library was just made re-syncs and re-refreshes, so an extra Create click on a filled library
                // neither syncs twice nor queues two refreshes.
                var result = await _reconciler.ApplyAsync(library, previous: null, isNew: false, renamedFrom: null);

                // Task 7's guarantee stays explicit here: queue the refresh only when the reconciler did not
                // already (9a-6 — exactly one refresh per Create).
                var directory = library.GetDirectory(DiscoverLibraries.GetRootDirectory());
                var refreshQueued = result.RefreshQueued
                    || await _refreshService.QueueLibraryRefreshAsync(directory, createMode: true, removeMode: false, refreshImages: true);

                var message = result.Ok
                    ? $"Library '{library.Name}' is ready. {result.SyncMessage}"
                        + (refreshQueued
                            ? string.Empty
                            : " The targeted refresh could not be queued — the library fills on the next sync or library scan.")
                    : result.Message ?? $"Could not create the library '{library.Name}'.";
                return Ok(new
                {
                    success = result.Ok,
                    message,
                    jellyfinLibraryId = result.JellyfinLibraryId,
                    syncMessage = result.SyncMessage,
                    refreshQueued
                });
            });
        }

        /// <summary>
        /// Sync one library only: the discover loop's per-library body for it, then its favorites hide/ignore
        /// step, then the usual refresh.
        /// </summary>
        /// <param name="name">The library name, matched ignoring case.</param>
        [HttpPost("Libraries/{name}/Sync")]
        public async Task<IActionResult> SyncLibrary(string name)
        {
            return await ExecuteWriteAsync("Sync Discover Library", async () =>
            {
                if (FindLibrary(name) == null)
                {
                    return NotFound(new { success = false, message = $"No Discover library named '{name}'." });
                }

                var syncResult = await _syncService.SyncLibraryAsync(name);
                _logger.LogInformation("Sync of library '{Name}' completed:\n{SyncResult}", name, syncResult.ToString());
                return Ok(new { success = syncResult.Success, message = syncResult.Message, result = syncResult.Message });
            });
        }

        /// <summary>
        /// Rename a library: validate, store the new name, and hand the whole job to the reconciler (task 9
        /// reviewer item 2) — folder move, Jellyfin library re-create with the new display name (ILibraryManager
        /// has no rename; this is the path the task 9 decision chose: new id, watch marks on placeholders reset)
        /// and the follow-up sync. POST Libraries applies the same for a renamedFrom entry; the page no longer
        /// needs the core POST /Library/VirtualFolders/Name call.
        /// </summary>
        /// <param name="name">The library's current name, matched ignoring case.</param>
        /// <param name="newName">The new name.</param>
        [HttpPost("Libraries/{name}/Rename")]
        public async Task<IActionResult> RenameLibrary(string name, [FromQuery] string? newName)
        {
            return await ExecuteWriteAsync("Rename Discover Library", async () =>
            {
                var library = FindLibrary(name);
                if (library == null)
                {
                    return NotFound(new { success = false, message = $"No Discover library named '{name}'." });
                }

                if (string.IsNullOrWhiteSpace(newName))
                {
                    return BadRequest(new { success = false, message = "A new name is required." });
                }

                var trimmed = newName.Trim();
                if (string.Equals(library.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(new { success = true, message = $"Library '{library.Name}' already has that name." });
                }

                if (DiscoverLibraries.GetAll().Any(l => l != library && string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { success = false, message = $"Library names must be unique ignoring case: \"{trimmed}\" is already used." });
                }

                // Same folder-name rules as POST Libraries: the new name must leave a usable folder that no other
                // library already uses.
                var newFolderName = FolderUtils.SanitizeFileName(trimmed);
                if (string.IsNullOrWhiteSpace(newFolderName) || newFolderName == "." || newFolderName == "..")
                {
                    return BadRequest(new { success = false, message = $"The name \"{trimmed}\" cannot be used: it leaves no usable folder name." });
                }

                if (DiscoverLibraries.GetAll().Any(l => l != library && !string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(FolderUtils.SanitizeFileName(l.Name), newFolderName, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { success = false, message = $"Different library names cannot share a folder: another library already makes the folder \"{newFolderName}\"." });
                }

                // 9a-b finding 1a: the new name must not belong to a Jellyfin library outside the bridge root
                // either — the same gate POST Libraries runs for renamed cards.
                if (FindForeignNameClash(trimmed) is string clashMessage)
                {
                    return BadRequest(new { success = false, message = clashMessage });
                }

                // Task 9: Rename is a thin wrapper over the reconciler (reviewer item 2) — it moves the folder,
                // re-creates the Jellyfin library with the new display name (new id, LatestItemsExcludes swap;
                // placeholder watch marks reset — what the editor warns about) and syncs the library.
                // 9a-b finding 3: PendingRenameFrom persists the old name — if the reconcile below is refused
                // (something is playing) or fails, the next sync's safety net retries; the reconciler clears it.
                library.Name = trimmed;
                library.PendingRenameFrom = name;
                Plugin.Instance?.SaveConfiguration();
                var result = await _reconciler.ApplyAsync(library, previous: library, isNew: false, renamedFrom: name);
                _logger.LogInformation("Renamed library '{Old}' to '{New}' (steps: {Steps}).", name, trimmed, string.Join(", ", result.Steps));
                return Ok(new
                {
                    success = result.Ok,
                    message = result.Ok ? $"Renamed library '{name}' to '{trimmed}'." : result.Message ?? $"Renaming to '{trimmed}' failed.",
                    jellyfinLibraryId = result.JellyfinLibraryId,
                    steps = result.Steps
                });
            });
        }

        /// <summary>
        /// Delete a library: remove it from the config, remove its Jellyfin library, and with
        /// <c>deleteFiles=true</c> also delete its folder. The Default library can't be deleted (400).
        /// </summary>
        /// <param name="name">The library name, matched ignoring case.</param>
        /// <param name="deleteFiles">Also delete the library's folder.</param>
        [HttpDelete("Libraries/{name}")]
        public async Task<IActionResult> DeleteLibrary(string name, [FromQuery] bool deleteFiles = false)
        {
            return await ExecuteWriteAsync("Delete Discover Library", async () =>
            {
                var library = FindLibrary(name);
                if (library == null)
                {
                    return NotFound(new { success = false, message = $"No Discover library named '{name}'." });
                }

                if (library.DefaultOrDefault)
                {
                    return BadRequest(new { success = false, message = "The Default library cannot be deleted. Tick Default on another library first." });
                }

                var root = DiscoverLibraries.GetRootDirectory();
                var directory = library.GetDirectory(root);
                var jellyfinLibrary = FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), directory);

                // Config first — after this the loops and syncs stop touching the library.
                var config = Plugin.GetConfiguration();
                config.DiscoverLibraries?.RemoveAll(l => ReferenceEquals(l, library)
                    || string.Equals(l.Name, library.Name, StringComparison.OrdinalIgnoreCase));
                Plugin.Instance?.SaveConfiguration();

                if (jellyfinLibrary != null)
                {
                    try
                    {
                        await _libraryManager.Inner.RemoveVirtualFolder(jellyfinLibrary.Name, refreshLibrary: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not remove the Jellyfin library {Library}", jellyfinLibrary.Name);
                        return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"The library left the config, but removing Jellyfin library '{jellyfinLibrary.Name}' failed: {ex.Message}" });
                    }

                    // 5c review fix 7: a deleted library's id must not linger in the users' LatestItemsExcludes.
                    await RemoveFromLatestItemsExcludesAsync(jellyfinLibrary.ItemId);
                }

                var filesDeleted = false;
                if (deleteFiles && Directory.Exists(directory))
                {
                    try
                    {
                        Directory.Delete(directory, true);
                        filesDeleted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not delete the folder {Directory} of library {Library}", directory, library.Name);
                        return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"Could not delete the library folder {directory}: {ex.Message}" });
                    }
                }

                _logger.LogInformation("Deleted library '{Name}'{Files}.", library.Name, filesDeleted ? " including its files" : string.Empty);
                return Ok(new { success = true, message = $"Deleted library '{library.Name}'.", filesDeleted });
            });
        }

        /// <summary>
        /// Shared wrapper for every write endpoint: run under the sync lock and refuse (409) while the libraries
        /// migration is pending or running.
        /// </summary>
        private Task<IActionResult> ExecuteWriteAsync(string operationName, Func<IActionResult> operation) =>
            ExecuteWriteAsync(operationName, () => Task.FromResult<IActionResult>(operation()));

        /// <summary>
        /// Shared wrapper for every write endpoint (async body): run under the sync lock and refuse (409) while
        /// the libraries migration is pending or running.
        /// </summary>
        private async Task<IActionResult> ExecuteWriteAsync(string operationName, Func<Task<IActionResult>> operation)
        {
            try
            {
                var result = await Plugin.ExecuteWithLockAsync<IActionResult?>(async () =>
                {
                    var refusal = MigrationRefusal();
                    if (refusal != null)
                    {
                        _logger.LogWarning("Refusing {Operation}: the libraries migration has not finished", operationName);
                        return refusal;
                    }

                    return await operation();
                }, _logger, operationName);

                // ExecuteWithLockAsync returns null when the duplicate request was skipped.
                return result ?? StatusCode(StatusCodes.Status429TooManyRequests, new { success = false, message = $"{operationName} is already queued." });
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("{Operation} timed out waiting for the sync lock", operationName);
                return StatusCode(StatusCodes.Status408RequestTimeout, new { success = false, message = $"{operationName} timed out while waiting for the sync lock." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed", operationName);
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"{operationName} failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Gets the 409 every write endpoint returns before the libraries migration has run: nothing may be
        /// edited while it is running or while <see cref="PluginConfiguration.LibrariesMigrationVersion"/> is 0,
        /// since the stored list is not used before the folders are moved.
        /// </summary>
        private ObjectResult? MigrationRefusal()
        {
            if (LibraryMigrationService.IsMigrationInProgress())
            {
                return StatusCode(StatusCodes.Status409Conflict, new { success = false, message = "The libraries migration is in progress; edit the Discover libraries afterwards." });
            }

            if ((Plugin.GetConfiguration().LibrariesMigrationVersion ?? 0) < 1)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { success = false, message = "The libraries migration has not run yet; the Discover libraries cannot be edited before it." });
            }

            return null;
        }

        /// <summary>
        /// Validates a whole library list.
        /// </summary>
        /// <param name="libraries">The list to validate.</param>
        /// <returns>The error message, or null when the list is valid.</returns>
        private static string? ValidateLibraryList(List<DiscoverLibraryConfig>? libraries)
        {
            if (libraries == null || libraries.Count == 0)
            {
                return "At least one library is required.";
            }

            if (libraries.Any(l => string.IsNullOrWhiteSpace(l.Name)))
            {
                return "Every library needs a name.";
            }

            var duplicate = libraries.GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                return $"Library names must be unique ignoring case: \"{duplicate.Key}\" is used more than once.";
            }

            var defaults = libraries.Count(l => l.DefaultOrDefault);
            if (defaults != 1)
            {
                return $"Exactly one library must be the Default library (found {defaults}).";
            }

            if (libraries.First(l => l.DefaultOrDefault).ListType == DiscoverListType.Trending)
            {
                return "The Default library cannot use the Trending list.";
            }

            var trendingWithNetworks = libraries.FirstOrDefault(l => l.ListType == DiscoverListType.Trending && l.NetworkIds.Count > 0);
            if (trendingWithNetworks != null)
            {
                return $"A Trending library cannot have networks: \"{trendingWithNetworks.Name}\".";
            }

            // Folder names come from the library name via SanitizeFileName. A name that sanitizes to empty, "."
            // or ".." would make the library folder the Library Directory itself or its parent (the sync would
            // write titles straight into it and deleteFiles would delete the root); two different names can also
            // sanitize to the same folder, so two libraries would share one folder.
            var folderByOwner = new List<KeyValuePair<string, string>>();
            foreach (var library in libraries)
            {
                var folderName = FolderUtils.SanitizeFileName(library.Name);
                if (string.IsNullOrWhiteSpace(folderName) || folderName == "." || folderName == "..")
                {
                    return $"The name \"{library.Name}\" cannot be used: it leaves no usable folder name.";
                }

                var clash = folderByOwner.FirstOrDefault(kv => string.Equals(kv.Key, folderName, StringComparison.OrdinalIgnoreCase));
                if (clash.Key != null)
                {
                    return $"Different library names cannot share a folder: \"{clash.Value}\" and \"{library.Name}\" both make the folder \"{folderName}\".";
                }

                folderByOwner.Add(new KeyValuePair<string, string>(folderName, library.Name));
            }

            return null;
        }

        /// <summary>
        /// Gets the configured library with this name, matched ignoring case.
        /// </summary>
        private static DiscoverLibraryConfig? FindLibrary(string name) =>
            DiscoverLibraries.GetAll().FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Gets the Jellyfin library that has this folder among its paths, or null.
        /// </summary>
        private static VirtualFolderInfo? FindVirtualFolder(IEnumerable<VirtualFolderInfo> virtualFolders, string directory) =>
            virtualFolders.FirstOrDefault(folder => (folder.Locations ?? Array.Empty<string>()).Any(location => FolderUtils.ArePathsEqual(location, directory)));

        /// <summary>
        /// 9a-b finding 1a: returns the 400 message when a Jellyfin library whose folders all sit OUTSIDE the
        /// bridge root — a library of the user's own, not JellyBridge's — already goes by this name, else null.
        /// A card taking such a name would block the reconciler forever (it must never repoint, remove or
        /// re-create the user's library), so the POST endpoints refuse it up front and the reconciler's own
        /// <c>FindBridgeJellyfinLibrary</c> gate refuses the apply.
        /// </summary>
        private string? FindForeignNameClash(string name)
        {
            var clash = _libraryManager.Inner.GetVirtualFolders().FirstOrDefault(folder =>
                string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)
                && !DiscoverLibraryReconciler.IsBridgeVirtualFolder(folder));
            return clash == null
                ? null
                : $"A Jellyfin library named '{name}' already exists outside JellyBridge (folder {string.Join(", ", clash.Locations ?? Array.Empty<string>())}) — rename that library or this card.";
        }

        /// <summary>
        /// Counts a library's titles on disk: the folders under it that hold a metadata.json (season folders
        /// inside a show hold none). 0 when the folder doesn't exist yet.
        /// </summary>
        private int CountTitlesOnDisk(string directory)
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
        /// Best-effort removal of a library id from the users' LatestItemsExcludes, for a deleted or re-created library (5c review
        /// fix 7): remove its id from every user's LatestItemsExcludes. A stale id is harmless (the library is
        /// gone), so per-user failures are only logged.
        /// </summary>
        private async Task RemoveFromLatestItemsExcludesAsync(string? itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !Guid.TryParse(itemId, out var libraryId))
            {
                _logger.LogWarning("Could not clean the users' LatestItemsExcludes for the deleted library: no library id.");
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

        /// <summary>
        /// The GET Libraries payload, for save responses that echo the new state.
        /// </summary>
        private List<object> GetLibrariesValue()
        {
            var root = DiscoverLibraries.GetRootDirectory();
            var virtualFolders = _libraryManager.Inner.GetVirtualFolders();
            return DiscoverLibraries.GetAll().Select(library =>
            {
                var directory = library.GetDirectory(root);
                var jellyfinLibrary = FindVirtualFolder(virtualFolders, directory);
                return (object)new
                {
                    library,
                    name = library.Name,
                    directory,
                    // 5c review fix 5: jellyfinLibraryName comes from the path-matched virtual folder even while
                    // its ItemId is still empty (after a rename the id can lag until the next scan) — the page
                    // shows "Create library" only when the name is null.
                    jellyfinLibraryId = jellyfinLibrary?.ItemId,
                    jellyfinLibraryName = jellyfinLibrary?.Name,
                    titleCount = CountTitlesOnDisk(directory)
                };
            }).ToList();
        }
    }
}
