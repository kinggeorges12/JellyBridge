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

        public DiscoverLibrariesController(
            ILoggerFactory loggerFactory,
            SyncService syncService,
            RefreshService refreshService,
            JellyfinILibraryManager libraryManager,
            JellyfinIUserManager userManager)
        {
            _logger = new DebugLogger<DiscoverLibrariesController>(loggerFactory.CreateLogger<DiscoverLibrariesController>());
            _syncService = syncService;
            _refreshService = refreshService;
            _libraryManager = libraryManager;
            _userManager = userManager;
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
        /// Save the whole library list. Validation (each failure → 400 with a message): at least one library,
        /// names unique ignoring case, exactly one Default, Default not Trending, Trending libraries without
        /// networks, usable folder names that don't collide. Names are the identity: a POST that drops a stored
        /// library is rejected — removals go through Delete and renames through Rename, so folders and Jellyfin
        /// libraries are never orphaned. The Default library is always saved enabled (decision 7).
        /// </summary>
        /// <param name="libraries">The complete new list.</param>
        [HttpPost("Libraries")]
        public async Task<IActionResult> SaveLibraries([FromBody] List<DiscoverLibraryConfig>? libraries)
        {
            return await ExecuteWriteAsync("Save Discover Libraries", () =>
            {
                var error = ValidateLibraryList(libraries);
                if (error != null)
                {
                    return BadRequest(new { success = false, message = error });
                }

                // Names are the library's identity: a stored name missing from the posted list means the client
                // deleted or renamed a library, which would orphan its folder and Jellyfin library. New names are
                // fine (added libraries; the page then calls Create for them).
                var storedNames = (Plugin.GetConfiguration().DiscoverLibraries ?? new List<DiscoverLibraryConfig>())
                    .Select(l => l.Name)
                    .ToList();
                var postedNames = new HashSet<string>(libraries!.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                var droppedName = storedNames.FirstOrDefault(n => !postedNames.Contains(n));
                if (droppedName != null)
                {
                    return BadRequest(new { success = false, message = $"Use Delete to remove '{droppedName}' and Rename to rename it." });
                }

                // Decision 7: the Default library is always on — force it, whatever the client posted.
                libraries!.First(l => l.DefaultOrDefault).IsEnabled = true;

                var config = Plugin.GetConfiguration();
                var previousByName = new Dictionary<string, DiscoverLibraryConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var previous in config.DiscoverLibraries ?? new List<DiscoverLibraryConfig>())
                {
                    previousByName[previous.Name] = previous;
                }

                foreach (var library in libraries!)
                {
                    // The editor round-trips the libraries it read, lastSyncedListType included; when a client
                    // leaves it out of a library that existed before, keep the stored value so decision 9's
                    // "the List changed" detection survives the save.
                    if (library.LastSyncedListType == null
                        && previousByName.TryGetValue(library.Name, out var previous)
                        && previous.LastSyncedListType != null)
                    {
                        library.LastSyncedListType = previous.LastSyncedListType;
                    }
                }

                config.DiscoverLibraries = libraries!;
                Plugin.Instance?.SaveConfiguration();
                _logger.LogInformation("Discover libraries saved: {Libraries}",
                    string.Join(", ", libraries!.Select(l => $"{l.Name} [{l.ListType}{(l.DefaultOrDefault ? ", Default" : string.Empty)}]")));
                return Ok(new { success = true, message = $"Saved {libraries.Count} Discover libraries.", libraries = GetLibrariesValue() });
            });
        }

        /// <summary>
        /// Create a library's folder (seeded with a .seed file, because Jellyfin skips empty folders) and the
        /// Jellyfin library pointing at it, with the Default library's Jellyfin options (paths replaced). The new
        /// library is added to every user's LatestItemsExcludes so its placeholders stay out of Continue Watching.
        /// Then it fills itself (task 7): a sync of just this library, followed by a targeted refresh of its
        /// CollectionFolder, so the titles appear without waiting for the next interval sync or a library scan.
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

                var directory = library.GetDirectory(DiscoverLibraries.GetRootDirectory());
                try
                {
                    Directory.CreateDirectory(directory);
                    var seedFile = Path.Combine(directory, ".seed");
                    if (!System.IO.File.Exists(seedFile))
                    {
                        await System.IO.File.WriteAllTextAsync(seedFile, "JellyBridge library folder — the seed keeps Jellyfin from skipping it while it is empty.\n");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not create the folder {Directory} of library {Library}", directory, library.Name);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"Could not create the folder {directory}: {ex.Message}" });
                }

                var jellyfinLibrary = FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), directory);
                if (jellyfinLibrary == null)
                {
                    var options = DefaultLibraryOptions();
                    options.PathInfos = new[] { new MediaPathInfo { Path = directory } };
                    var collectionType = library.LibraryType switch
                    {
                        DiscoverLibraryType.Movies => CollectionTypeOptions.movies,
                        DiscoverLibraryType.Series => CollectionTypeOptions.tvshows,
                        _ => (CollectionTypeOptions?)null
                    };

                    try
                    {
                        // refreshLibrary: true on purpose: on 10.10 the CollectionFolder (and with it
                        // VirtualFolderInfo.ItemId) only materializes once a scan has run over the folder, and the
                        // folder is empty except the seed, so that scan is trivial.
                        await _libraryManager.Inner.AddVirtualFolder(library.Name, collectionType, options, refreshLibrary: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not create the Jellyfin library for {Library}", library.Name);
                        return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"Could not create the Jellyfin library '{library.Name}': {ex.Message}" });
                    }

                    jellyfinLibrary = await FindVirtualFolderWithIdAsync(directory);
                    if (jellyfinLibrary != null)
                    {
                        await AddToLatestItemsExcludesAsync(jellyfinLibrary.ItemId);
                    }

                    _logger.LogInformation("Created Jellyfin library '{Name}' on {Path}.", library.Name, directory);
                }
                else
                {
                    _logger.LogInformation("Library '{Name}' already has a Jellyfin library on {Path}.", library.Name, directory);
                }

                // Task 7: fill the library now instead of waiting for the next interval sync. The sync runs
                // inside this same write lock (the caller holds it; SyncLibraryAsync does not re-acquire it),
                // then a targeted refresh of only this library's CollectionFolder makes Jellyfin materialize
                // the titles — the same thing POST /Items/<libraryId>/Refresh?Recursive=true does, which is
                // what filled both stuck libraries live within 10 s. A "Scan All Libraries" plan would be
                // skipped while Jellyfin's Scan Media Library task is already running (every restart, 15+ min
                // on this server), which is exactly why the new library stayed empty.
                var syncResult = await _syncService.SyncLibraryAsync(library.Name);
                _logger.LogInformation("Create: sync of library '{Name}' completed:\n{SyncResult}", library.Name, syncResult.ToString());
                var refreshQueued = await _refreshService.QueueLibraryRefreshAsync(directory, createMode: true, removeMode: false, refreshImages: true);

                var message = $"Library '{library.Name}' is ready. {syncResult.Message}"
                    + (refreshQueued ? string.Empty : " The targeted refresh could not be queued — the library fills on the next sync or library scan.");
                return Ok(new
                {
                    success = true,
                    message,
                    jellyfinLibraryId = jellyfinLibrary?.ItemId,
                    syncMessage = syncResult.Message,
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
        /// Rename a library: move its folder, then update the Jellyfin library that points at it (add the new
        /// path before removing the old one) and save the config. The Jellyfin library's display name is renamed
        /// from the settings page with the REST call POST /Library/VirtualFolders/Name — ILibraryManager has no
        /// rename method.
        /// </summary>
        /// <param name="name">The library's current name, matched ignoring case.</param>
        /// <param name="newName">The new name.</param>
        [HttpPost("Libraries/{name}/Rename")]
        public async Task<IActionResult> RenameLibrary(string name, [FromQuery] string? newName)
        {
            return await ExecuteWriteAsync("Rename Discover Library", () =>
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

                var root = DiscoverLibraries.GetRootDirectory();
                var oldDirectory = library.GetDirectory(root);
                var newDirectory = FolderUtils.GetNormalizedPath(Path.Combine(root, FolderUtils.SanitizeFileName(trimmed)))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Move the folder first, so the new path exists before the Jellyfin library points at it.
                if (Directory.Exists(oldDirectory) && !FolderUtils.ArePathsEqual(oldDirectory, newDirectory))
                {
                    try
                    {
                        Directory.Move(oldDirectory, newDirectory);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not move the folder of library {Library} from {Old} to {New}", library.Name, oldDirectory, newDirectory);
                        return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"Could not move the library folder: {ex.Message}" });
                    }
                }

                // Repoint the Jellyfin library (add the new path before removing the old one; never remove the
                // old path when adding the new one failed).
                var jellyfinLibrary = FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), oldDirectory);
                if (jellyfinLibrary != null && !FolderUtils.ArePathsEqual(oldDirectory, newDirectory))
                {
                    var locations = (jellyfinLibrary.Locations ?? Array.Empty<string>()).ToList();
                    try
                    {
                        if (!locations.Any(location => FolderUtils.ArePathsEqual(location, newDirectory)))
                        {
                            _libraryManager.Inner.AddMediaPath(jellyfinLibrary.Name, new MediaPathInfo { Path = newDirectory });
                        }

                        if (locations.Any(location => FolderUtils.ArePathsEqual(location, oldDirectory)))
                        {
                            _libraryManager.Inner.RemoveMediaPath(jellyfinLibrary.Name, oldDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Repoint of Jellyfin library {Library} ({Old} -> {New}) failed during rename", jellyfinLibrary.Name, oldDirectory, newDirectory);
                        return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"The folder moved, but repointing Jellyfin library '{jellyfinLibrary.Name}' failed: {ex.Message}" });
                    }
                }

                library.Name = trimmed;
                Plugin.Instance?.SaveConfiguration();
                _logger.LogInformation("Renamed library '{Old}' to '{New}' ({OldPath} -> {NewPath}).", name, trimmed, oldDirectory, newDirectory);
                return Ok(new { success = true, message = $"Renamed library '{name}' to '{trimmed}'.", jellyfinLibraryId = jellyfinLibrary?.ItemId });
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
        /// Like <see cref="FindVirtualFolder"/>, but retries until the Jellyfin library has a real ItemId: after a
        /// fresh AddVirtualFolder the folder listing can briefly carry the library without its id (10.10 fills it
        /// only once the collection folder materializes), and the id is needed for LatestItemsExcludes and the response.
        /// </summary>
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
        /// Builds the LibraryOptions for a new Jellyfin library: the Default library's options (shallow copy, the
        /// caller replaces PathInfos), or a quiet default when the Default library has no Jellyfin library yet.
        /// </summary>
        private LibraryOptions DefaultLibraryOptions()
        {
            var defaultLibrary = DiscoverLibraries.GetDefault();
            var defaultInfo = defaultLibrary == null
                ? null
                : FindVirtualFolder(_libraryManager.Inner.GetVirtualFolders(), defaultLibrary.GetDirectory(DiscoverLibraries.GetRootDirectory()));
            if (defaultInfo != null && Guid.TryParse(defaultInfo.ItemId, out var defaultId))
            {
                var defaultFolder = _libraryManager.Inner.GetItemById(defaultId);
                var options = defaultFolder == null ? null : _libraryManager.Inner.GetLibraryOptions(defaultFolder);
                if (options != null)
                {
                    // Round-trip copy: only the copy's PathInfos is replaced, the Default library keeps its own paths.
                    return System.Text.Json.JsonSerializer.Deserialize<LibraryOptions>(
                        System.Text.Json.JsonSerializer.Serialize(options))!;
                }
            }

            return new LibraryOptions { EnableRealtimeMonitor = false };
        }

        /// <summary>
        /// Adds a library's id to every user's LatestItemsExcludes, the same as the live server does for the
        /// Discover/Trending libraries — otherwise the new library's placeholders show up in Continue Watching.
        /// </summary>
        private async Task AddToLatestItemsExcludesAsync(string? itemId)
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
                    // The User entity has no Configuration property on any supported version; the UserDto carries
                    // the same UserConfiguration UpdateConfigurationAsync takes, so read, change and write that.
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
        /// Best-effort counterpart of <see cref="AddToLatestItemsExcludesAsync"/> for a deleted library (5c review
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
