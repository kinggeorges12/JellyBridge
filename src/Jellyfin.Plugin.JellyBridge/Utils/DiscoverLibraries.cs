using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;

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
    /// Gets every configured library, or the ones synthesized from the previous single-library settings when the
    /// migration hasn't persisted any. Never empty; the stored list is returned as stored.
    /// </summary>
    public static List<DiscoverLibraryConfig> GetAll()
    {
        var config = Plugin.GetConfiguration();
        var configured = config.DiscoverLibraries
            ?.Where(library => !string.IsNullOrWhiteSpace(library.Name))
            .ToList();

        if (configured is { Count: > 0 })
        {
            return configured;
        }

        return SynthesizeLegacy(config);
    }

    /// <summary>
    /// Gets the enabled libraries the sync should run for.
    /// </summary>
    public static List<DiscoverLibraryConfig> GetEnabled()
    {
        return GetAll().Where(library => library.EnabledOrDefault).ToList();
    }

    /// <summary>
    /// Gets the default library: titles opened from search land here and it can't be deleted. The stored flag wins;
    /// without exactly one, the first library is the default.
    /// </summary>
    public static DiscoverLibraryConfig GetDefault()
    {
        var libraries = GetAll();
        return libraries.FirstOrDefault(library => library.DefaultOrDefault) ?? libraries[0];
    }

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

        if (Plugin.GetRawConfigOrDefault<bool>(nameof(PluginConfiguration.UseTrendingLibrary), config)
            && TrendingLibrary.GetLegacyDirectory() is { } trendingDirectory)
        {
            libraries.Add(new()
            {
                Name = TrendingLibrary.Tag,
                ListType = DiscoverListType.Trending,
                LibraryType = DiscoverLibraryType.All,
                IsEnabled = true,
                Order = DiscoverLibraryOrder.AsList,
                DirectoryOverride = trendingDirectory,
                Legacy = true,
                Pages = Plugin.GetRawConfigOrDefault<int>(nameof(PluginConfiguration.TrendingPages), config)
            });
        }

        return libraries;
    }
}
