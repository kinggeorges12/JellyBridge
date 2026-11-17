using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.JellyBridge.BridgeModels.BridgeConfiguration;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// One planned folder move: a title folder and the folder it should live in after the migration.
/// </summary>
/// <param name="Source">The title folder as it is today.</param>
/// <param name="Target">The title folder inside its library's folder.</param>
public sealed record MigrationMove(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target);

/// <summary>
/// One planned Jellyfin library repoint: a library keeps its identity, only its folder changes.
/// </summary>
/// <param name="LibraryName">The Jellyfin library's name.</param>
/// <param name="OldPath">The path the library points at today.</param>
/// <param name="NewPath">The path it should point at after the migration.</param>
public sealed record MigrationRepoint(
    [property: JsonPropertyName("libraryName")] string LibraryName,
    [property: JsonPropertyName("oldPath")] string OldPath,
    [property: JsonPropertyName("newPath")] string NewPath);

/// <summary>
/// What the libraries migration would do: the new library list, every title folder move, every Jellyfin
/// library repoint, the copies to delete and the warnings to show. Produced by
/// <see cref="LibraryMigrationService.Plan"/> — reading it only lists folders, it never moves anything.
/// </summary>
public sealed class MigrationPlan
{
    /// <summary>The Discover libraries the migration would store.</summary>
    [JsonPropertyName("libraries")]
    public List<DiscoverLibraryConfig> Libraries { get; } = new();

    /// <summary>Every title folder move, one entry per title folder.</summary>
    [JsonPropertyName("moves")]
    public List<MigrationMove> Moves { get; } = new();

    /// <summary>The Jellyfin library repoints, one per old Jellyfin library.</summary>
    [JsonPropertyName("repoints")]
    public List<MigrationRepoint> Repoints { get; } = new();

    /// <summary>Title folders to delete because another copy wins (Network Folders with duplicate content).</summary>
    [JsonPropertyName("duplicates")]
    public List<string> Duplicates { get; } = new();

    /// <summary>Text lines for the log and the settings-page banner.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; } = new();

    /// <summary>True when the settings-page banner should be shown after the migration (Network Folders users).</summary>
    [JsonPropertyName("showMigrationNotice")]
    public bool ShowMigrationNotice { get; set; }

    /// <summary>True when the planner refused the migration (a target folder exists and is not a JellyBridge folder).
    /// The executor must then move nothing and save nothing — it logs the warnings and retries next startup.</summary>
    [JsonPropertyName("aborted")]
    public bool Aborted { get; set; }

    /// <summary>True when the plan has nothing to do: refused, or no library list could be built. A plan with a
    /// library list but no moves (a fresh install, an empty folder with no Jellyfin library) is NOT empty — the
    /// executor still saves the list and version 1, so such a server does not run the legacy synthesis forever.</summary>
    [JsonIgnore]
    public bool IsEmpty => Aborted || Libraries.Count == 0;
}

/// <summary>
/// Moves the pre-libraries layout (one Library Directory, optional Movies/Shows split, optional network
/// subfolders, optional standalone Trending folder) into one folder per Discover library, and plans the
/// matching Jellyfin library repoints. <see cref="Plan"/> is read-only — it only lists folders and reads
/// metadata.json, never moves, creates or deletes anything — so the plan can be logged, fetched over the
/// API and tested before the executor acts on it.
/// </summary>
public class LibraryMigrationService
{
    private const string MetadataFileName = "metadata.json";
    private const string IgnoreFileName = ".ignore";

    /// <summary>The resume journal, written beside the Library Directory before the first move and deleted when
    /// the migration finishes. While it exists on an un-migrated server every sync refuses to run.</summary>
    public const string JournalFileName = ".jellybridge-migration.json";

    private readonly DebugLogger<LibraryMigrationService> _logger;
    private readonly SyncService _syncService;
    private readonly RefreshService _refreshService;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly FavoriteService _favoriteService;

    public LibraryMigrationService(
        ILogger<LibraryMigrationService> logger,
        SyncService syncService,
        RefreshService refreshService,
        JellyfinILibraryManager libraryManager,
        FavoriteService favoriteService)
    {
        _logger = new DebugLogger<LibraryMigrationService>(logger);
        _syncService = syncService;
        _refreshService = refreshService;
        _libraryManager = libraryManager;
        _favoriteService = favoriteService;
    }

    /// <summary>
    /// Builds the migration plan for the given configuration and the current Jellyfin libraries.
    /// Already-migrated servers (<see cref="PluginConfiguration.LibrariesMigrationVersion"/> ≥ 1) get an empty plan.
    /// </summary>
    /// <param name="config">The plugin configuration to migrate from (read with defaults, never written).</param>
    /// <param name="jellyfinLibraries">The Jellyfin virtual folders, for naming libraries and planning repoints.</param>
    /// <returns>The plan.</returns>
    public MigrationPlan Plan(PluginConfiguration config, IReadOnlyList<VirtualFolderInfo> jellyfinLibraries)
    {
        var plan = new MigrationPlan();
        if ((config.LibrariesMigrationVersion ?? 0) >= 1)
        {
            return plan;
        }

        var root = Plugin.GetRawConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory), config)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            plan.Warnings.Add($"Library Directory does not exist ({root}); nothing to migrate.");
            return plan;
        }

        var mixed = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseMixedMediaLibrary), config);
#pragma warning disable CS0618 // UseNetworkFolders/AddDuplicateContent/NetworkFolderPrefix are read only by this migration
        var useNetworkFolders = Plugin.GetRawConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders), config);
        var addDuplicateContent = Plugin.GetRawConfigOrDefault<bool>(nameof(PluginConfiguration.AddDuplicateContent), config);
        var networkPrefix = Plugin.GetRawConfigOrDefault<string>(nameof(PluginConfiguration.NetworkFolderPrefix), config);
#pragma warning restore CS0618
        var networkMap = Plugin.GetRawConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap), config)
            ?? new List<JellyseerrNetwork>();
        var networkIds = networkMap.Select(network => network.Id).ToList();
        var networkFolderNames = networkMap
            .Where(network => !string.IsNullOrWhiteSpace(network.Name))
            .Select(network => FolderUtils.SanitizeFileName(networkPrefix + network.Name))
            .ToList();

        // The legacy standalone trending folder (same rule as the synthesized legacy libraries): the configured
        // one, or "<root> Trending". Null when it overlaps the Library Directory — then no trending titles move.
        var trendingDirectory = LegacyTrendingDirectory(config, root);

        // --- Build the new library list ----------------------------------------
        var order = DiscoverLibraryConfig.OrderFromLegacySettings(
            Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableAutomatedSortTask), config),
            Plugin.GetConfigOrDefault<SortOrderOptions>(nameof(PluginConfiguration.SortOrder), config));
        var pages = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxDiscoverPages), config);
        var minimumRating = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.MinimumRating), config);
        var minimumVotes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MinimumVotes), config);
        var retentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays), config);

        var libraries = new List<DiscoverLibraryConfig>();

        DiscoverLibraryConfig AddLibrary(string name, DiscoverLibraryType type, bool isDefault)
        {
            var library = new DiscoverLibraryConfig
            {
                Name = name,
                ListType = DiscoverListType.Popular,
                LibraryType = type,
                IsDefault = isDefault,
                IsEnabled = true,
                NetworkIds = networkIds,
                Pages = pages,
                MinimumRating = minimumRating,
                MinimumVotes = minimumVotes,
                RetentionDays = retentionDays,
                Order = order
            };
            libraries.Add(library);
            return library;
        }

        // Named after the Jellyfin library that points at each old folder (decision 9), with fallbacks.
        var moviesSource = Path.Combine(root, "Movies");
        var showsSource = Path.Combine(root, "Shows");
        var discoverLibrary = mixed
            ? AddLibrary(JellyfinLibraryNameFor(jellyfinLibraries, root) ?? DiscoverLibraries.DefaultName, DiscoverLibraryType.All, true)
            : null;
        var moviesLibrary = mixed
            ? null
            : AddLibrary(JellyfinLibraryNameFor(jellyfinLibraries, moviesSource) ?? "Discover Movies", DiscoverLibraryType.Movies, true);
        var showsLibrary = mixed
            ? null
            : AddLibrary(JellyfinLibraryNameFor(jellyfinLibraries, showsSource) ?? "Discover Series", DiscoverLibraryType.Series, false);

        if (useNetworkFolders && networkMap.Count == 0)
        {
            plan.Warnings.Add("Network Folders was on but no networks are configured; network folders cannot be identified and will not be moved.");
        }

        DiscoverLibraryConfig? trendingLibrary = null;
        if (Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTrendingLibrary), config))
        {
            if (trendingDirectory == null)
            {
                plan.Warnings.Add("Trending Directory must be outside the Library Directory; Trending library skipped.");
            }
            else
            {
                trendingLibrary = new DiscoverLibraryConfig
                {
                    Name = JellyfinLibraryNameFor(jellyfinLibraries, trendingDirectory) ?? DiscoverLibraryConfig.TrendingTag,
                    ListType = DiscoverListType.Trending,
                    LibraryType = DiscoverLibraryType.All,
                    IsEnabled = true,
                    Order = DiscoverLibraryOrder.AsList,
                    Pages = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TrendingPages), config)
                };
                libraries.Add(trendingLibrary);
            }
        }
        else if (trendingDirectory != null && TitleFolders(trendingDirectory).Count > 0)
        {
            plan.Warnings.Add($"Titles exist in the Trending Directory ({trendingDirectory}) but the Trending library is off; they will not be moved.");
        }

        EnsureUniqueNames(libraries, plan);
        plan.Libraries.AddRange(libraries);
        plan.ShowMigrationNotice = useNetworkFolders;

        // Which library each old source folder's titles move into.
        var libraryForSource = new Dictionary<string, DiscoverLibraryConfig?>(StringComparer.Ordinal)
        {
            [Normalize(root)] = discoverLibrary
        };
        if (!mixed)
        {
            libraryForSource[Normalize(moviesSource)] = moviesLibrary;
            libraryForSource[Normalize(showsSource)] = showsLibrary;
        }

        if (trendingLibrary != null && trendingDirectory != null)
        {
            libraryForSource[Normalize(trendingDirectory)] = trendingLibrary;
        }

        // --- Walk the sources and plan the moves --------------------------------
        var planned = new List<(string Source, string Target, DiscoverLibraryConfig Library)>();

        foreach (var (sourceFolder, owner) in libraryForSource)
        {
            var source = sourceFolder.TrimEnd(Path.DirectorySeparatorChar);
            if (owner == null)
            {
                // Separate mode: the Library Directory root itself owns no library folder. Titles and
                // leftovers sitting directly in it are not part of the planned moves — report them.
                foreach (var stray in StraysAtSource(source, root, mixed, networkFolderNames, trendingDirectory))
                {
                    plan.Warnings.Add($"Left in place ({Path.Combine(source, stray)}): not a title folder.");
                }

                foreach (var unmoved in TitleFolders(source))
                {
                    plan.Warnings.Add($"Not moved ({unmoved}): a title folder in the Library Directory root of a separate Movies/Shows setup.");
                }

                continue;
            }

            var targetFolder = owner.GetDirectory(root);
            var targetCheck = CheckTarget(source, targetFolder, root, networkFolderNames);
            if (targetCheck == 2)
            {
                plan.Aborted = true;
                plan.Warnings.Add($"Cannot migrate: {targetFolder} exists and is not a JellyBridge folder. No files will be moved.");
                continue;
            }

            // A target folder equal to the source (separate mode with a library literally named "Movies":
            // the target root/Movies IS the source) means every title is already in place — no moves.
            var titlesAlreadyInPlace = FolderUtils.ArePathsEqual(source, targetFolder);
            foreach (var titleFolder in TitleFolders(source))
            {
                if (titlesAlreadyInPlace)
                {
                    continue;
                }

                planned.Add((titleFolder, Path.Combine(targetFolder, Path.GetFileName(titleFolder)), owner));
            }

            if (useNetworkFolders)
            {
                foreach (var networkFolderName in networkFolderNames)
                {
                    var networkFolder = Path.Combine(source, networkFolderName);
                    if (!Directory.Exists(networkFolder))
                    {
                        continue;
                    }

                    foreach (var titleFolder in TitleFolders(networkFolder))
                    {
                        planned.Add((titleFolder, Path.Combine(targetFolder, Path.GetFileName(titleFolder)), owner));
                    }
                }
            }

            foreach (var stray in StraysAtSource(source, root, mixed, networkFolderNames, trendingDirectory))
            {
                plan.Warnings.Add($"Left in place ({Path.Combine(source, stray)}): not a title folder.");
            }
        }

        // --- Duplicate copies (Network Folders) ----------------------------------
        var duplicatePaths = new HashSet<string>(StringComparer.Ordinal);
        if (useNetworkFolders)
        {
            foreach (var group in planned
                         .Select(entry => (entry, identity: ReadMetadataIdentity(entry.Source)))
                         .Where(pair => pair.identity != null)
                         .GroupBy(pair => $"{pair.identity!.Value.MediaType}:{pair.identity.Value.TmdbId}", StringComparer.Ordinal))
            {
                if (group.Count() < 2)
                {
                    continue;
                }

                // The copy without .ignore wins, then the newest metadata.json.
                var copies = group
                    .OrderBy(pair => File.Exists(Path.Combine(pair.entry.Source, IgnoreFileName)) ? 1 : 0)
                    .ThenByDescending(pair => File.GetLastWriteTimeUtc(Path.Combine(pair.entry.Source, MetadataFileName)))
                    .ToList();
                for (var i = 1; i < copies.Count; i++)
                {
                    plan.Duplicates.Add(copies[i].entry.Source);
                    duplicatePaths.Add(copies[i].entry.Source);
                }

                plan.Warnings.Add($"{Path.GetFileName(copies[0].entry.Source)}: {copies.Count} copies found"
                    + (addDuplicateContent ? string.Empty : " (duplicate content was off)")
                    + $", keeping one in {copies[0].entry.Library.FolderName}.");
            }
        }

        foreach (var (source, target, _) in planned)
        {
            if (!duplicatePaths.Contains(source))
            {
                plan.Moves.Add(new MigrationMove(source, target));
            }
        }

        // A refused target aborts the whole plan — never move half the library list.
        if (plan.Aborted)
        {
            plan.Moves.Clear();
            plan.Duplicates.Clear();
            plan.Repoints.Clear();
            return plan;
        }

        // --- Repoints -------------------------------------------------------------
        foreach (var library in libraries)
        {
            var oldPaths = library.ListType == DiscoverListType.Trending
                ? (trendingDirectory != null ? new List<string> { trendingDirectory } : new List<string>())
                : mixed
                    ? new List<string> { root }
                    : library.LibraryType == DiscoverLibraryType.Movies
                        ? new List<string> { moviesSource }
                        : new List<string> { showsSource };
            if (oldPaths.Count == 0)
            {
                continue;
            }

            var newPath = library.GetDirectory(root);
            var matched = jellyfinLibraries.Where(lib => oldPaths.Any(old => PointsAt(lib, old))).ToList();
            if (matched.Count == 0)
            {
                plan.Warnings.Add($"No Jellyfin library points at {oldPaths[0]}; create a library named {library.Name} on {newPath} after the migration.");
                continue;
            }

            if (matched.Count > 1)
            {
                plan.Warnings.Add($"Several Jellyfin libraries point at {oldPaths[0]}: {string.Join(", ", matched.Select(m => m.Name))}. All of them are repointed.");
            }

            // Every library on an old path gets repointed, not only the first — the rest would keep pointing at
            // an emptied folder. One repoint per path the library actually holds (Network Folders libraries hold
            // one per network), so the executor adds the new path and removes exactly the old ones.
            foreach (var matchedLibrary in matched)
            {
                var currentPaths = (matchedLibrary.Locations ?? Array.Empty<string>())
                    .Select(location => location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .Where(location => oldPaths.Any(old => FolderUtils.ArePathsEqual(location, old) || FolderUtils.IsPathInDirectory(location, old)))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (currentPaths.Count == 0)
                {
                    currentPaths = oldPaths;
                }

                foreach (var oldPath in currentPaths)
                {
                    plan.Repoints.Add(new MigrationRepoint(matchedLibrary.Name, oldPath, newPath));
                }
            }
        }

        return plan;
    }

    /// <summary>
    /// True while a half-finished libraries migration journal exists on an un-migrated server: the discover
    /// sync, the favorites sync, cleanup and the sort must refuse (4c) — never sync half-migrated folders.
    /// </summary>
    public static bool IsMigrationInProgress()
    {
        if ((Plugin.GetConfiguration().LibrariesMigrationVersion ?? 0) >= 1)
        {
            return false;
        }

        var root = DiscoverLibraries.GetRootDirectory();
        return !string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, JournalFileName));
    }

    /// <summary>
    /// Runs the libraries migration once (4b): flush favorites to Jellyseerr on the old layout (only when the
    /// server actually has favorites — a server without a reachable Seerr and no favorites must still migrate),
    /// journal the plan,
    /// move every title folder into its library folder, delete the losing duplicate copies, repoint the old
    /// Jellyfin libraries (add the new path before removing the old one), save the new library list with
    /// <see cref="PluginConfiguration.LibrariesMigrationVersion"/> 1, delete the journal, remove now-empty legacy
    /// folders and queue one library scan. A crash mid-migration leaves the journal behind, and the next startup
    /// resumes from it instead of re-planning. Already-migrated servers return without touching anything.
    /// The caller must hold <see cref="Plugin.ExecuteWithLockAsync"/>.
    /// </summary>
    /// <returns>The outcome; when aborted nothing was moved or saved and the migration retries next startup.</returns>
    public async Task<MigrationResult> RunAsync()
    {
        try
        {
            return await RunCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyBridge libraries migration failed - it will retry on the next startup.");
            return new MigrationResult { Aborted = true, Message = ex.Message };
        }
    }

    private async Task<MigrationResult> RunCoreAsync()
    {
        var config = Plugin.GetConfiguration();
        var root = DiscoverLibraries.GetRootDirectory();
        var journalPath = string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, JournalFileName);

        if ((config.LibrariesMigrationVersion ?? 0) >= 1)
        {
            if (journalPath != null && File.Exists(journalPath))
            {
                // Crashed between saving the configuration and deleting the journal — the migration is committed.
                DeleteFileSafe(journalPath);
            }

            return new MigrationResult { Message = "Already migrated." };
        }

        MigrationJournal state;
        if (journalPath == null)
        {
            _logger.LogWarning("JellyBridge libraries migration: the Library Directory is not configured; nothing to migrate.");
            return new MigrationResult { Message = "Library Directory is not configured." };
        }

        if (File.Exists(journalPath))
        {
            var loaded = LoadJournal(journalPath);
            if (loaded == null)
            {
                _logger.LogError("The JellyBridge migration journal ({Journal}) could not be read; nothing will be moved. "
                    + "Fix or remove the journal manually, then restart.", journalPath);
                return new MigrationResult { Aborted = true, Message = "Corrupt migration journal." };
            }

            state = loaded;
            _logger.LogInformation("Resuming the JellyBridge libraries migration from {Journal}: {Done}/{Total} title moves done, configSaved={ConfigSaved}.",
                journalPath, state.DoneMoves.Count, state.Plan.Moves.Count, state.ConfigSaved);
        }
        else
        {
            var newPlan = Plan(config, _libraryManager.Inner.GetVirtualFolders());
            // On a server with nothing to migrate (a fresh install whose Library Directory does not exist yet)
            // these lines are routine startup noise, not warnings.
            foreach (var warning in newPlan.Warnings)
            {
                if (newPlan.Aborted || newPlan.Libraries.Count > 0)
                {
                    _logger.LogWarning("{Warning}", warning);
                }
                else
                {
                    _logger.LogInformation("{Warning}", warning);
                }
            }

            if (newPlan.Aborted)
            {
                _logger.LogWarning("JellyBridge libraries migration refused by the planner - nothing moved, retrying on the next startup.");
                return new MigrationResult { Aborted = true, Message = string.Join("; ", newPlan.Warnings) };
            }

            if (newPlan.Libraries.Count == 0)
            {
                _logger.LogInformation("JellyBridge libraries migration: nothing to migrate.");
                return new MigrationResult { Message = "Nothing to migrate." };
            }

            LogPlan(newPlan);

            // Moved titles get new Jellyfin item ids, so a favorite that was never requested would be lost —
            // request all favorites first, on the old layout. Only the request step runs: after task 3 the
            // hide/ignore step computes flat paths, which is wrong for a Network Folders layout, and the next
            // sync after the migration hides the requested titles in the new layout anyway.
            // The flush needs a reachable Jellyseerr, so run it only when there is actually something to
            // lose — a server with no Seerr connection and no favorites must still migrate.
            var favorites = _favoriteService.GetUserFavorites();
            if (favorites.Count > 0)
            {
                var flush = await _syncService.RequestFavoritesStepAsync();
                if (!flush.Success)
                {
                    _logger.LogWarning("Favorites flush failed ({Message}) - libraries migration aborted, retrying on the next startup.", flush.Message);
                    return new MigrationResult { Aborted = true, Message = $"Favorites flush failed: {flush.Message}" };
                }
            }
            else
            {
                _logger.LogInformation("JellyBridge libraries migration: no favorites found; skipping the Jellyseerr favorites flush.");
            }

            state = new MigrationJournal { Plan = newPlan };
            SaveJournal(journalPath, state);
        }

        var plan = state.Plan;

        // Move each title folder (same drive: a rename — artwork, promos, .ignore and NFOs ride along).
        var doneMoves = new HashSet<string>(state.DoneMoves, StringComparer.Ordinal);
        foreach (var move in plan.Moves)
        {
            if (doneMoves.Contains(move.Source))
            {
                continue;
            }

            MoveTitleFolder(move.Source, move.Target);
            doneMoves.Add(move.Source);
            state.DoneMoves.Add(move.Source);
            SaveJournal(journalPath, state);
        }

        // Delete the losing duplicate copies (Network Folders with duplicate content).
        if (!state.DuplicatesDeleted)
        {
            foreach (var duplicate in plan.Duplicates)
            {
                DeleteDirectorySafe(duplicate, "duplicate copy");
            }

            state.DuplicatesDeleted = true;
            SaveJournal(journalPath, state);
        }

        // Repoint the old Jellyfin libraries — always add the new path before removing the old one.
        var repointedLibraries = new HashSet<string>(StringComparer.Ordinal);
        var doneRepoints = new HashSet<string>(state.DoneRepoints, StringComparer.Ordinal);
        var failedRepoints = new List<string>();
        foreach (var repoint in plan.Repoints)
        {
            var key = $"{repoint.LibraryName}\n{repoint.OldPath}";
            if (!doneRepoints.Contains(key))
            {
                if (FolderUtils.ArePathsEqual(repoint.OldPath, repoint.NewPath))
                {
                    // The library already points at its target folder (e.g. a library named like its old
                    // folder). Doing nothing is correct; removing the "old" path would leave it with none.
                    _logger.LogInformation("Repoint skipped for Jellyfin library {LibraryName}: it already points at {Path}.",
                        repoint.LibraryName, repoint.NewPath);
                    repointedLibraries.Add(repoint.LibraryName);
                    continue;
                }

                try
                {
                    var locations = (_libraryManager.Inner.GetVirtualFolders()
                            .FirstOrDefault(folder => string.Equals(folder.Name, repoint.LibraryName, StringComparison.Ordinal))?.Locations
                            ?? Array.Empty<string>()).ToList();
                    if (!locations.Any(location => FolderUtils.ArePathsEqual(location, repoint.NewPath)))
                    {
                        _libraryManager.Inner.AddMediaPath(repoint.LibraryName, new MediaPathInfo { Path = repoint.NewPath });
                        _logger.LogInformation("Repoint: added {NewPath} to Jellyfin library {LibraryName}.", repoint.NewPath, repoint.LibraryName);
                    }

                    if (locations.Any(location => FolderUtils.ArePathsEqual(location, repoint.OldPath)))
                    {
                        _libraryManager.Inner.RemoveMediaPath(repoint.LibraryName, repoint.OldPath);
                        _logger.LogInformation("Repoint: removed {OldPath} from Jellyfin library {LibraryName}.", repoint.OldPath, repoint.LibraryName);
                    }
                }
                catch (Exception ex)
                {
                    // Keep the other repoints going; a failed one leaves that library on the old path. Record it
                    // in the config (LibrariesMigrationIssues + the banner flag) so the settings page can tell the
                    // user to fix it by hand. Never remove the old path when adding the new one failed.
                    _logger.LogError(ex, "Repoint of Jellyfin library {LibraryName} ({OldPath} -> {NewPath}) failed.",
                        repoint.LibraryName, repoint.OldPath, repoint.NewPath);
                    failedRepoints.Add($"Jellyfin library {repoint.LibraryName} still points at {repoint.OldPath}; add {repoint.NewPath} to it");
                }

                state.DoneRepoints.Add(key);
                SaveJournal(journalPath, state);
            }

            repointedLibraries.Add(repoint.LibraryName);
        }

        // Persist failed repoints so the user sees which Jellyfin libraries still need a hand (the migration
        // itself keeps going — the titles are already in their new folders).
        if (failedRepoints.Count > 0)
        {
            foreach (var issue in failedRepoints)
            {
                if (!config.LibrariesMigrationIssues.Contains(issue, StringComparer.Ordinal))
                {
                    config.LibrariesMigrationIssues.Add(issue);
                }
            }

            config.ShowLibrariesMigrationNotice = true;
            if (state.ConfigSaved)
            {
                // Resumed run whose config was already saved — store the issues straight away.
                Plugin.Instance.SaveConfiguration();
            }
        }

        // Save the new library list. The old settings stay untouched in the XML so a downgrade still has them.
        if (!state.ConfigSaved)
        {
            config.DiscoverLibraries = plan.Libraries;
            config.LibrariesMigrationVersion = 1;
            if (plan.ShowMigrationNotice)
            {
                config.ShowLibrariesMigrationNotice = true;
            }

            Plugin.Instance.SaveConfiguration();
            state.ConfigSaved = true;
            SaveJournal(journalPath, state);
        }

        // The migration is committed — remove the journal so syncs are no longer refused.
        DeleteFileSafe(journalPath);

        RemoveEmptyLegacyFolders(config, root, plan);

        // Queue one library scan so the repointed libraries pick the moved titles up.
        await _refreshService.ScanAllLibraries(force: true);

        _logger.LogInformation(
            "JellyBridge libraries migration done: {Libraries} libraries, {Titles} titles moved, {Repoints} Jellyfin libraries repointed",
            plan.Libraries.Count, state.DoneMoves.Count, repointedLibraries.Count);
        return new MigrationResult
        {
            Executed = true,
            TitlesMoved = state.DoneMoves.Count,
            DuplicatesDeleted = plan.Duplicates.Count,
            RepointedLibraries = repointedLibraries.Count,
            Message = "Migration complete."
        };
    }

    /// <summary>Logs the full plan at INFO before anything moves.</summary>
    private void LogPlan(MigrationPlan plan)
    {
        _logger.LogInformation(
            "JellyBridge libraries migration plan: {Libraries} libraries ({Names}), {Moves} title moves, {Repoints} Jellyfin library repoints, {Duplicates} duplicate copies.",
            plan.Libraries.Count,
            string.Join(", ", plan.Libraries.Select(library => $"{library.Name} ({library.LibraryType}{(library.DefaultOrDefault ? ", default" : string.Empty)})")),
            plan.Moves.Count, plan.Repoints.Count, plan.Duplicates.Count);

        foreach (var group in plan.Moves
                     .GroupBy(move => (From: Path.GetDirectoryName(move.Source) ?? string.Empty, To: Path.GetDirectoryName(move.Target) ?? string.Empty))
                     .OrderBy(group => group.Key.From, StringComparer.Ordinal))
        {
            _logger.LogInformation("  {From} -> {To}: {Count} titles", group.Key.From, group.Key.To, group.Count());
        }

        foreach (var repoint in plan.Repoints)
        {
            _logger.LogInformation("  repoint {LibraryName}: add {NewPath}, then remove {OldPath}", repoint.LibraryName, repoint.NewPath, repoint.OldPath);
        }

        foreach (var duplicate in plan.Duplicates)
        {
            _logger.LogInformation("  delete duplicate copy {Path}", duplicate);
        }
    }

    /// <summary>
    /// Moves one title folder. <see cref="Directory.Move"/> is a rename on the same filesystem; on cross-device
    /// <see cref="IOException"/> fall back to copy + delete per folder. A target that already exists (a crash
    /// between the fallback's copy and delete) gets its contents merged.
    /// </summary>
    private void MoveTitleFolder(string source, string target)
    {
        if (FolderUtils.ArePathsEqual(source, target))
        {
            // The title is already exactly where the migration wants it (a library named like its old folder).
            return;
        }

        if (!Directory.Exists(source))
        {
            _logger.LogWarning("Migration move skipped: {Source} no longer exists.", source);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(target))
        {
            CopyDirectoryContents(source, target);
            DeleteDirectorySafe(source, "moved title folder");
            return;
        }

        try
        {
            Directory.Move(source, target);
        }
        catch (IOException)
        {
            CopyDirectoryContents(source, target);
            DeleteDirectorySafe(source, "moved title folder");
        }
    }

    private static void CopyDirectoryContents(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var child = Path.Combine(target, Path.GetFileName(directory));
            Directory.CreateDirectory(child);
            CopyDirectoryContents(directory, child);
        }
    }

    /// <summary>Removes the legacy Movies/Shows folders, the network folders and the old standalone trending
    /// folder — only when they are empty (a stray the planner left behind keeps the folder and the warning).</summary>
    private void RemoveEmptyLegacyFolders(PluginConfiguration config, string root, MigrationPlan plan)
    {
        var libraryFolders = new HashSet<string>(plan.Libraries.Select(library => Normalize(library.GetDirectory(root))), StringComparer.Ordinal);
        var moviesSource = Path.Combine(root, "Movies");
        var showsSource = Path.Combine(root, "Shows");
        var candidates = new List<string> { moviesSource, showsSource };

        var trendingDirectory = LegacyTrendingDirectory(config, root);
        if (trendingDirectory != null)
        {
            candidates.Add(trendingDirectory);
        }

        // Network folders sat directly in the sources before task 3 removed the feature.
#pragma warning disable CS0618
        var networkPrefix = Plugin.GetRawConfigOrDefault<string>(nameof(PluginConfiguration.NetworkFolderPrefix), config);
        var networkNames = (Plugin.GetRawConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap), config) ?? new List<JellyseerrNetwork>())
            .Where(network => !string.IsNullOrWhiteSpace(network.Name))
            .Select(network => FolderUtils.SanitizeFileName(networkPrefix + network.Name))
            .ToList();
#pragma warning restore CS0618
        foreach (var networkName in networkNames)
        {
            candidates.Add(Path.Combine(root, networkName));
            candidates.Add(Path.Combine(moviesSource, networkName));
            candidates.Add(Path.Combine(showsSource, networkName));
        }

        foreach (var candidate in candidates)
        {
            if (libraryFolders.Contains(Normalize(candidate)) || FolderUtils.ArePathsEqual(candidate, root))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(candidate) && !Directory.EnumerateFileSystemEntries(candidate).Any())
                {
                    Directory.Delete(candidate);
                    _logger.LogInformation("Removed now-empty legacy folder {Path}.", candidate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not check or remove legacy folder {Path}.", candidate);
            }
        }
    }

    private static MigrationJournal? LoadJournal(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MigrationJournal>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void SaveJournal(string path, MigrationJournal state)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    private void DeleteDirectorySafe(string path, string what)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete {What} {Path}.", what, path);
        }
    }

    private static void DeleteFileSafe(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Left behind — the already-migrated check keeps the syncs running, and the next startup retries.
        }
    }

    /// <summary>
    /// The legacy standalone trending folder: the configured one, or "&lt;root&gt; Trending". Null when it
    /// overlaps the Library Directory.
    /// </summary>
    private static string? LegacyTrendingDirectory(PluginConfiguration config, string root)
    {
        var trending = string.IsNullOrWhiteSpace(config.TrendingDirectory)
            ? root + " " + DiscoverLibraryConfig.TrendingTag
            : config.TrendingDirectory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (FolderUtils.IsPathInDirectory(trending, root) || FolderUtils.IsPathInDirectory(root, trending))
        {
            return null;
        }

        return trending;
    }

    /// <summary>
    /// The Jellyfin library that points at the given path: the name to reuse for the migrated library.
    /// </summary>
    private static string? JellyfinLibraryNameFor(IReadOnlyList<VirtualFolderInfo> jellyfinLibraries, string path)
    {
        return jellyfinLibraries.FirstOrDefault(lib => PointsAt(lib, path))?.Name;
    }

    /// <summary>
    /// True when a Jellyfin library points at the given path or at a folder inside it — the old Network Folders
    /// page created one library holding every network path. Never matches a library on a parent folder: a server
    /// whose only Jellyfin library holds /media (everything) is unrelated to the JellyBridge root — naming a
    /// migrated library after it and adding the library folder to that unrelated library would both be wrong.
    /// </summary>
    private static bool PointsAt(VirtualFolderInfo library, string path)
    {
        return library.Locations?.Any(location =>
            FolderUtils.ArePathsEqual(location, path)
            || FolderUtils.IsPathInDirectory(location, path)) == true;
    }

    /// <summary>
    /// Directories directly under <paramref name="folder"/> that hold a metadata.json — the title folders.
    /// Never uses MetadataService: after task 3 it only resolves the flat post-migration layout.
    /// </summary>
    private static List<string> TitleFolders(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return new List<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(title => File.Exists(Path.Combine(title, MetadataFileName)))
                .ToList();
        }
        catch (IOException)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// The entries of a source folder that are neither title folders nor known structure (Movies/Shows, network
    /// folders, the sibling trending folder) — the planner leaves them in place and warns about them.
    /// </summary>
    private static List<string> StraysAtSource(string sourceFolder, string root, bool mixed, List<string> networkFolderNames, string? trendingDirectory)
    {
        var strays = new List<string>();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(sourceFolder);
        }
        catch (IOException)
        {
            return strays;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var isDirectory = Directory.Exists(entry);
            if (isDirectory && (File.Exists(Path.Combine(entry, MetadataFileName)) || networkFolderNames.Contains(name, StringComparer.Ordinal)))
            {
                continue;
            }

            if (sourceFolder == root && !mixed && (name.Equals("Movies", StringComparison.Ordinal) || name.Equals("Shows", StringComparison.Ordinal)))
            {
                continue;
            }

            if (sourceFolder == root && trendingDirectory != null && Normalize(entry) == Normalize(trendingDirectory))
            {
                continue;
            }

            strays.Add(name);
        }

        return strays;
    }

    /// <summary>
    /// Checks the library's target folder against the migration sources. 0 = free to move straight in,
    /// 1 = collides (the target is itself the source of the same titles, so the titles are already in place,
    /// or the target already holds title folders the moves merge into), 2 = the target exists, is not a
    /// JellyBridge folder and is not a source — the plan must abort.
    /// </summary>
    private static int CheckTarget(string sourceFolder, string targetFolder, string root, List<string> networkFolderNames)
    {
        if (string.Equals(Normalize(sourceFolder), Normalize(targetFolder), StringComparison.Ordinal))
        {
            // e.g. separate mode with a library literally named "Movies": target root/Movies is the source itself.
            return 1;
        }

        if (!Directory.Exists(targetFolder) || string.Equals(Normalize(targetFolder), Normalize(root), StringComparison.Ordinal))
        {
            return 0;
        }

        var titleFolders = TitleFolders(targetFolder);
        if (titleFolders.Count > 0 || titleFolders.Any(folder => networkFolderNames.Contains(Path.GetFileName(folder), StringComparer.Ordinal)))
        {
            // The moves merge into the folders already there (MoveTitleFolder merges an existing target).
            return 1;
        }

        // An unrelated non-empty folder (no JellyBridge markers) must not be moved into.
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(targetFolder).ToList();
            if (entries.Count == 0)
            {
                return 0;
            }

            var jellyBridgeMarkers = new HashSet<string>(StringComparer.Ordinal) { MetadataFileName, IgnoreFileName, ".seed" };
            return Directory.EnumerateFileSystemEntries(targetFolder)
                .Select(Path.GetFileName)
                .All(name => name != null && jellyBridgeMarkers.Contains(name))
                ? 0
                : 2;
        }
        catch (IOException)
        {
            return 2;
        }
    }

    /// <summary>
    /// (mediaType, tmdb id) from a title folder's metadata.json, or null when it can't be read.
    /// </summary>
    private static (int MediaType, int TmdbId)? ReadMetadataIdentity(string titleFolder)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(titleFolder, MetadataFileName)));
            var rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("id", out var id) || !rootElement.TryGetProperty("mediaType", out var mediaType))
            {
                return null;
            }

            return (mediaType.GetInt32(), id.GetInt32());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the library names unique ignoring case by appending " 2", " 3", … on a clash (decision 9).
    /// </summary>
    private static void EnsureUniqueNames(List<DiscoverLibraryConfig> libraries, MigrationPlan plan)
    {
        var seen = new List<string>();
        foreach (var library in libraries)
        {
            var name = library.Name;
            var suffix = 2;
            while (seen.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{library.Name} {suffix++}";
            }

            if (!string.Equals(name, library.Name, StringComparison.Ordinal))
            {
                plan.Warnings.Add($"Library name \"{library.Name}\" clashes (ignoring case) with an existing one; renamed to \"{name}\".");
                library.Name = name;
            }

            seen.Add(name);
        }
    }

    private static string Normalize(string path) => FolderUtils.GetNormalizedPath(path);
}

/// <summary>Outcome of <see cref="LibraryMigrationService.RunAsync"/>.</summary>
public sealed class MigrationResult
{
    /// <summary>True when the migration ran and saved the library list with version 1.</summary>
    [JsonPropertyName("executed")]
    public bool Executed { get; init; }

    /// <summary>True when the migration refused or failed — nothing was moved or saved, and it retries next startup.</summary>
    [JsonPropertyName("aborted")]
    public bool Aborted { get; init; }

    /// <summary>Title folders moved (total, including moves done before a crash-and-resume).</summary>
    [JsonPropertyName("titlesMoved")]
    public int TitlesMoved { get; init; }

    /// <summary>Duplicate copies deleted (Network Folders with duplicate content).</summary>
    [JsonPropertyName("duplicatesDeleted")]
    public int DuplicatesDeleted { get; init; }

    /// <summary>Distinct Jellyfin libraries repointed.</summary>
    [JsonPropertyName("repointedLibraries")]
    public int RepointedLibraries { get; init; }

    /// <summary>Short human-readable outcome.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// The on-disk resume journal (<c>&lt;root&gt;/.jellybridge-migration.json</c>): the plan being executed plus
/// the per-move and per-repoint done flags. Its existence on an un-migrated server marks the migration
/// half-done, which makes every sync refuse (4c).
/// </summary>
public sealed class MigrationJournal
{
    /// <summary>The plan being executed (re-used on resume instead of re-planning).</summary>
    [JsonPropertyName("plan")]
    public MigrationPlan Plan { get; set; } = new();

    /// <summary>The move sources completed so far.</summary>
    [JsonPropertyName("doneMoves")]
    public List<string> DoneMoves { get; set; } = new();

    /// <summary>True when the duplicate copies were deleted.</summary>
    [JsonPropertyName("duplicatesDeleted")]
    public bool DuplicatesDeleted { get; set; }

    /// <summary>The completed repoints, keyed "library name\nold path".</summary>
    [JsonPropertyName("doneRepoints")]
    public List<string> DoneRepoints { get; set; } = new();

    /// <summary>True when the library list and <see cref="PluginConfiguration.LibrariesMigrationVersion"/> 1 were saved.</summary>
    [JsonPropertyName("configSaved")]
    public bool ConfigSaved { get; set; }
}
