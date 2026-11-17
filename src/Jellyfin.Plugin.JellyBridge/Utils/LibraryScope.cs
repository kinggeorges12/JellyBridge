using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// Runs the sync, favorite requests, cleanup and sorting steps for one Discover library. Inside <see cref="Enter"/>,
/// <see cref="Plugin.GetConfigOrDefault{T}"/> answers with that library's folder, networks, list limits and sort
/// order instead of the global single-library settings — the way <see cref="TrendingLibrary.Enter"/> did for the
/// Trending library, generalized to any number of libraries.
/// </summary>
public static class LibraryScope
{
    private static readonly AsyncLocal<DiscoverLibraryConfig?> _current = new();

    /// <summary>
    /// Gets the library the current code runs for, or null outside <see cref="Enter"/>.
    /// </summary>
    public static DiscoverLibraryConfig? Current => _current.Value;

    /// <summary>
    /// Gets a value indicating whether the current code runs inside <see cref="Enter"/>.
    /// </summary>
    public static bool IsActive => _current.Value != null;

    /// <summary>
    /// Run the following code (until disposed) for one Discover library.
    /// </summary>
    /// <param name="library">The library to run for.</param>
    /// <returns>A token that restores the previous scope.</returns>
    public static IDisposable Enter(DiscoverLibraryConfig library)
    {
        var previous = _current.Value;
        _current.Value = library;
        return new Scope(previous);
    }

    /// <summary>
    /// Gets the networks of the current library: the global NetworkMap narrowed to the library's network ids. An empty
    /// network id list means all networks, and is returned as an empty list, which the fetcher treats as the "All"
    /// network. Outside a scope, the global NetworkMap.
    /// </summary>
    public static List<JellyseerrNetwork> GetNetworks()
    {
        var networkMap = Plugin.GetRawConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap));
        if (Current is not { } library)
        {
            return networkMap;
        }

        if (library.NetworkIds.Count == 0)
        {
            return new List<JellyseerrNetwork>();
        }

        return networkMap.Where(network => library.NetworkIds.Contains(network.Id)).ToList();
    }

    /// <summary>
    /// Settings that read differently inside <see cref="Enter"/>: they describe the current library's folder, networks,
    /// list and limits instead of the global single-library settings. Returning null falls through to the stored value.
    /// </summary>
    internal static bool TryGetOverride(string propertyName, out object? value)
    {
        value = null;
        if (_current.Value is not { } library)
        {
            return false;
        }

        var config = Plugin.GetConfiguration();
        object GetGlobal(string name) => Plugin.GetRawConfigOrDefault<object>(name, config);

        value = propertyName switch
        {
            nameof(PluginConfiguration.LibraryDirectory) => library.GetDirectory(DiscoverLibraries.GetRootDirectory()),

            // A library's folder is flat, and its networks are the library's own list. A Legacy library keeps the old
            // mixed/separate and network-folder layout until the migration moves its content into a subfolder.
            nameof(PluginConfiguration.UseMixedMediaLibrary) => library.Legacy ? GetGlobal(nameof(PluginConfiguration.UseMixedMediaLibrary)) : true,
            nameof(PluginConfiguration.UseNetworkFolders) => library.Legacy ? GetGlobal(nameof(PluginConfiguration.UseNetworkFolders)) : false,

            // The trending list is paged like the discover lists; at least one page (0 would mean all 500)
            nameof(PluginConfiguration.MaxDiscoverPages) => library.ListType == DiscoverListType.Trending
                ? Math.Max(1, library.Pages ?? Plugin.GetRawConfigOrDefault<int>(nameof(PluginConfiguration.TrendingPages), config))
                : (object?)library.Pages,

            nameof(PluginConfiguration.MinimumRating) => (object?)library.MinimumRating,
            nameof(PluginConfiguration.MinimumVotes) => (object?)library.MinimumVotes,
            nameof(PluginConfiguration.MaxRetentionDays) => (object?)library.RetentionDays,
            nameof(PluginConfiguration.NetworkMap) => GetNetworks(),

            // SortOrder is deliberately not overridden: task 2c reads the library's Order itself. Returning the
            // global here would let the sort task zero the fetch-position play counts "AsList" libraries rely on.

            _ => null
        };

        return value != null;
    }

    private sealed class Scope : IDisposable
    {
        private readonly DiscoverLibraryConfig? _previous;

        public Scope(DiscoverLibraryConfig? previous) => _previous = previous;

        public void Dispose() => _current.Value = _previous;
    }
}
