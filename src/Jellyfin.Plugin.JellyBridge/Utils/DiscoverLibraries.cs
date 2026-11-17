using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// The list of Discover libraries: each one a subfolder of the Library Directory and a Jellyfin library, filled by its
/// own Jellyseerr list, networks and settings. While the startup migration hasn't run (<see
/// cref="PluginConfiguration.LibrariesMigrationVersion"/> is 0), the list is synthesized from the previous
/// single-library settings with <see cref="DiscoverLibraryConfig.Legacy"/> set, so the per-library code keeps today's
/// layout. Reading the list never changes the stored configuration.
/// </summary>
public static class DiscoverLibraries
{
    /// <summary>
    /// The name of the synthesized default library.
    /// </summary>
    public const string DefaultName = "Discover";

    /// <summary>
    /// Gets every configured library. Before the migration (<see
    /// cref="PluginConfiguration.LibrariesMigrationVersion"/> is 0) this is always the list synthesized from the
    /// previous single-library settings — a stored list is ignored, so nothing syncs before the folders are moved.
    /// After it, only the stored list is used and a stored empty list means no libraries (logged, nothing syncs) —
    /// synthesizing then would sync titles into the Library Directory root.
    /// </summary>
    public static List<DiscoverLibraryConfig> GetAll()
    {
        var config = Plugin.GetConfiguration();

        if ((config.LibrariesMigrationVersion ?? 0) < 1)
        {
            return SynthesizeLegacy(config);
        }

        var stored = config.DiscoverLibraries
            ?.Where(library => !string.IsNullOrWhiteSpace(library.Name))
            .ToList() ?? new List<DiscoverLibraryConfig>();

        if (stored.Count == 0)
        {
            LogOnce("NoLibrariesConfigured", LogLevel.Error, "Migration is done but no Discover libraries are configured; nothing will sync.");
        }

        return stored;
    }

    /// <summary>
    /// Logs a message the first time it is asked for, then stays quiet — GetBridgeDirectories() runs on every
    /// playback and favorite event, and repeating the same warning there buries the log.
    /// </summary>
    private static void LogOnce(string key, LogLevel level, string message)
    {
        if (!_loggedOnceKeys.TryAdd(key, 1))
        {
            return;
        }

        Plugin.Instance?.LoggerFactory?.CreateLogger(typeof(DiscoverLibraries).FullName!)?.Log(level, message);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _loggedOnceKeys = new();
    private static int _loggedSynthesis;

    /// <summary>
    /// Gets the enabled libraries the sync should run for.
    /// </summary>
    public static List<DiscoverLibraryConfig> GetEnabled()
    {
        return GetAll().Where(library => library.EnabledOrDefault).ToList();
    }

    /// <summary>
    /// Gets the enabled Trending library, if one exists. Until the loop (task 1c) replaces the last fixed Trending
    /// call sites, this answers "is the Trending library turned on, and where is its folder".
    /// </summary>
    public static DiscoverLibraryConfig? GetTrending()
    {
        return GetEnabled().FirstOrDefault(library => library.ListType == DiscoverListType.Trending);
    }

    /// <summary>
    /// Gets the default library: titles opened from search land here and it can't be deleted. The stored flag wins;
    /// when none or several libraries claim it, the first library is the default (the odd case is logged once). Null
    /// only when no libraries are configured at all.
    /// </summary>
    /// <returns>The default library, or null when no libraries are configured.</returns>
    public static DiscoverLibraryConfig? GetDefault()
    {
        var libraries = GetAll();
        if (libraries.Count == 0)
        {
            return null;
        }

        var flagged = libraries.Where(library => library.DefaultOrDefault).ToList();
        if (flagged.Count == 1)
        {
            return flagged[0];
        }

        LogOnce(flagged.Count > 1 ? "MultipleDefaultLibraries" : "NoDefaultLibrary", LogLevel.Warning,
            flagged.Count > 1
                ? "Several Discover libraries are marked default; using the first one."
                : "No Discover library is marked default; using the first one.");
        return libraries[0];
    }

    /// <summary>
    /// Gets the library a title opened from search should be added to: the default library when its type fits the
    /// title, otherwise the first enabled library whose type fits, otherwise the default library anyway.
    /// </summary>
    /// <param name="isMovie">True to open a movie, false for a series.</param>
    /// <returns>The library to open into, or null when no libraries are configured.</returns>
    public static DiscoverLibraryConfig? GetForSearchOpen(bool isMovie)
    {
        var defaultLibrary = GetDefault();
        if (defaultLibrary == null)
        {
            return null;
        }

        if (FitsSearchOpen(defaultLibrary, isMovie))
        {
            return defaultLibrary;
        }

        return GetAll().FirstOrDefault(library => library.EnabledOrDefault && FitsSearchOpen(library, isMovie)) ?? defaultLibrary;
    }

    /// <summary>
    /// Gets a value indicating whether a library's type can hold the given media type.
    /// </summary>
    /// <param name="library">The library to check.</param>
    /// <param name="isMovie">True for a movie, false for a series.</param>
    /// <returns>True when the library can hold the title.</returns>
    private static bool FitsSearchOpen(DiscoverLibraryConfig library, bool isMovie) => library.LibraryType switch
    {
        DiscoverLibraryType.Movies => isMovie,
        DiscoverLibraryType.Series => !isMovie,
        _ => true
    };

    /// <summary>
    /// Gets the Library Directory that holds every library's subfolder. Reads the setting directly, so it is not
    /// affected by <see cref="LibraryScope.Enter"/>.
    /// </summary>
    public static string GetRootDirectory()
    {
        var config = Plugin.GetConfiguration();
        return Plugin.GetRawConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory), config)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Gets every folder JellyBridge owns: the folder of each library, enabled or not (a disabled library still holds
    /// titles), plus the Library Directory itself until the migration has moved its titles into the library subfolders.
    /// </summary>
    public static List<string> GetAllDirectories()
    {
        var config = Plugin.GetConfiguration();
        var root = GetRootDirectory();
        var directories = GetAll().Select(library => library.GetDirectory(root)).ToList();
        if ((config.LibrariesMigrationVersion ?? 0) < 1)
        {
            directories.Add(root);
        }

        return directories.DistinctBy(FolderUtils.GetNormalizedPath).ToList();
    }

    /// <summary>
    /// Builds the library list from the previous single-library settings: the whole Library Directory becomes the
    /// default library, and a turned-on Trending library becomes a Trending library at its own folder. Pure function
    /// of the settings, apart from the trending folder overlap check.
    /// </summary>
    private static List<DiscoverLibraryConfig> SynthesizeLegacy(PluginConfiguration config)
    {
        if (_loggedSynthesis == 0 && Plugin.Instance?.LoggerFactory != null)
        {
            Interlocked.Exchange(ref _loggedSynthesis, 1);
            Plugin.Instance.LoggerFactory.CreateLogger(typeof(DiscoverLibraries).FullName!).LogInformation(
                "Discover libraries migration has not run yet — using libraries synthesized from the single-library settings.");
        }

        var networkMap = Plugin.GetRawConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap), config);
        var libraries = new List<DiscoverLibraryConfig>
        {
            new()
            {
                Name = DefaultName,
                ListType = DiscoverListType.Popular,
                LibraryType = DiscoverLibraryType.All,
                IsDefault = true,
                IsEnabled = true,
                // Before the migration the library is the Library Directory itself, laid out the old way (Legacy)
                DirectoryOverride = GetRootDirectory(),
                Legacy = true,
                NetworkIds = networkMap.Select(network => network.Id).ToList(),
                Pages = Plugin.GetRawConfigOrDefault<int>(nameof(PluginConfiguration.MaxDiscoverPages), config),
                MinimumRating = Plugin.GetRawConfigOrDefault<double>(nameof(PluginConfiguration.MinimumRating), config),
                MinimumVotes = Plugin.GetRawConfigOrDefault<int>(nameof(PluginConfiguration.MinimumVotes), config),
                RetentionDays = Plugin.GetRawConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays), config)
                // Order stays unset: a Legacy library keeps sorting by the global SortOrder (LibraryScope)
            }
        };

        if (Plugin.GetRawConfigOrDefault<bool>(nameof(PluginConfiguration.UseTrendingLibrary), config))
        {
            if (LegacyTrendingDirectory(config) is { } trendingDirectory)
            {
                libraries.Add(new()
                {
                    Name = DiscoverLibraryConfig.TrendingTag,
                    ListType = DiscoverListType.Trending,
                    LibraryType = DiscoverLibraryType.All,
                    IsEnabled = true,
                    Order = DiscoverLibraryOrder.AsList,
                    DirectoryOverride = trendingDirectory,
                    // Not Legacy: the trending folder was always a flat mixed folder — exactly what a non-Legacy
                    // library is — so the scope must answer mixed=true and network folders=false, not the global values.
                    Pages = Plugin.GetRawConfigOrDefault<int>(nameof(PluginConfiguration.TrendingPages), config)
                });
            }
            else
            {
                // Used to be a visible sync failure ("Trending Directory must be outside the Library Directory");
                // the library is now simply skipped, so say so once.
                LogOnce("TrendingOverlap", LogLevel.Warning, "Trending Directory must be outside the Library Directory; Trending library skipped.");
            }
        }

        return libraries;
    }

    /// <summary>
    /// Gets the legacy standalone trending folder as the single-library settings configure it: the configured one,
    /// or "&lt;Library Directory&gt; Trending" next to it. Null when it would overlap the Library Directory.
    /// </summary>
    private static string? LegacyTrendingDirectory(PluginConfiguration config)
    {
        var libraryDirectory = string.IsNullOrWhiteSpace(config.LibraryDirectory)
            ? (string)PluginConfiguration.DefaultValues[nameof(PluginConfiguration.LibraryDirectory)]
            : config.LibraryDirectory;
        var trendingDirectory = string.IsNullOrWhiteSpace(config.TrendingDirectory)
            ? FolderUtils.GetNormalizedPath(libraryDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + " " + DiscoverLibraryConfig.TrendingTag
            : config.TrendingDirectory;

        if (FolderUtils.IsPathInDirectory(trendingDirectory, libraryDirectory) || FolderUtils.IsPathInDirectory(libraryDirectory, trendingDirectory))
        {
            return null;
        }

        return FolderUtils.GetNormalizedPath(trendingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
