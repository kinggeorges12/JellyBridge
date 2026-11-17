using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Model.Entities;
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

    /// <summary>Library names whose target folder collides with a source or an existing title folder, so their
    /// titles move through a temp folder <c>&lt;root&gt;/.jb-migrate-&lt;name&gt;</c> first and the temp folder
    /// is renamed at the end.</summary>
    [JsonPropertyName("stagedLibraries")]
    public List<string> StagedLibraries { get; } = new();

    /// <summary>True when the plan has nothing to do (already migrated, nothing configured, or refused after a warning).</summary>
    [JsonIgnore]
    public bool IsEmpty => Libraries.Count == 0 || (Moves.Count == 0 && Repoints.Count == 0);
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
                plan.Warnings.Add($"Cannot migrate: {targetFolder} exists and is not a JellyBridge folder. No files will be moved.");
                continue;
            }

            if (targetCheck == 1 && !plan.StagedLibraries.Contains(owner.Name, StringComparer.OrdinalIgnoreCase))
            {
                plan.StagedLibraries.Add(owner.Name);
            }

            foreach (var titleFolder in TitleFolders(source))
            {
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
        if (plan.Warnings.Any(w => w.StartsWith("Cannot migrate", StringComparison.Ordinal)))
        {
            plan.Moves.Clear();
            plan.Duplicates.Clear();
            plan.Repoints.Clear();
            plan.StagedLibraries.Clear();
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
                plan.Warnings.Add($"Several Jellyfin libraries point at {oldPaths[0]}: {string.Join(", ", matched.Select(m => m.Name))}. Only {matched[0].Name} is repointed.");
            }

            // One repoint per path the library actually holds (Network Folders libraries hold one per network),
            // so the executor adds the new path and removes exactly the old ones.
            var currentPaths = (matched[0].Locations ?? Array.Empty<string>())
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
                plan.Repoints.Add(new MigrationRepoint(matched[0].Name, oldPath, newPath));
            }
        }

        return plan;
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
    /// True when a Jellyfin library points at the given path (or at a parent/child of it, which is how
    /// the old Network Folders page created libraries: one library holding every network path).
    /// </summary>
    private static bool PointsAt(VirtualFolderInfo library, string path)
    {
        return library.Locations?.Any(location =>
            FolderUtils.ArePathsEqual(location, path)
            || FolderUtils.IsPathInDirectory(path, location)
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
    /// 1 = collides (the target is itself the source of the same titles, or already holds title folders)
    /// so the titles must be staged through a temp folder, 2 = the target exists, is not a JellyBridge
    /// folder and is not a source — the plan must abort.
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
