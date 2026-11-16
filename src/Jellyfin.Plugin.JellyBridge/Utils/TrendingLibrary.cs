using Jellyfin.Plugin.JellyBridge.Configuration;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// The optional Trending library: Jellyseerr's trending titles in their own folder, outside the Library Directory, so the
/// Discover libraries don't include them. The sync, favorite requests, cleanup and sorting run their usual steps a second
/// time inside <see cref="Enter"/>, where the Library Directory setting points at the trending folder, that folder is laid
/// out like a mixed library without network folders, and Discover Pages reads as Trending Pages.
/// </summary>
public static class TrendingLibrary
{
    /// <summary>
    /// Tag given to titles in the Trending library (stored like a network name).
    /// </summary>
    public const string Tag = "Trending";

    private static readonly AsyncLocal<bool> _active = new();

    /// <summary>
    /// Gets a value indicating whether the current code runs for the Trending library.
    /// </summary>
    public static bool IsActive => _active.Value;

    /// <summary>
    /// Gets a value indicating whether the Trending library is turned on.
    /// </summary>
    public static bool IsEnabled => Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTrendingLibrary));

    /// <summary>
    /// Run the following code (until disposed) for the Trending library.
    /// </summary>
    public static IDisposable Enter()
    {
        var previous = _active.Value;
        _active.Value = true;
        return new Scope(previous);
    }

    /// <summary>
    /// Gets the trending folder: the configured one, or "&lt;Library Directory&gt; Trending" next to the Library Directory.
    /// Returns null when the Trending library is off, or when the folder would overlap the Library Directory.
    /// </summary>
    public static string? GetDirectory()
    {
        if (!IsEnabled)
        {
            return null;
        }

        // Read the settings directly; inside Enter, GetConfigOrDefault returns the trending folder as the Library Directory
        var config = Plugin.GetConfiguration();
        var libraryDirectory = string.IsNullOrWhiteSpace(config.LibraryDirectory)
            ? (string)PluginConfiguration.DefaultValues[nameof(PluginConfiguration.LibraryDirectory)]
            : config.LibraryDirectory;
        var trendingDirectory = string.IsNullOrWhiteSpace(config.TrendingDirectory)
            ? FolderUtils.GetNormalizedPath(libraryDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + " " + Tag
            : config.TrendingDirectory;

        if (FolderUtils.IsPathInDirectory(trendingDirectory, libraryDirectory) || FolderUtils.IsPathInDirectory(libraryDirectory, trendingDirectory))
        {
            return null;
        }

        return FolderUtils.GetNormalizedPath(trendingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Settings that read differently inside <see cref="Enter"/>.
    /// </summary>
    internal static bool TryGetOverride(string propertyName, out object? value)
    {
        value = propertyName switch
        {
            nameof(PluginConfiguration.LibraryDirectory) => GetDirectory(),
            nameof(PluginConfiguration.UseMixedMediaLibrary) => true,
            nameof(PluginConfiguration.UseNetworkFolders) => false,
            // The trending list is paged like the discover lists; at least one page (0 would mean all 500)
            nameof(PluginConfiguration.MaxDiscoverPages) => Math.Max(1, Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TrendingPages))),
            _ => null,
        };
        return value != null;
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool _previous;

        public Scope(bool previous) => _previous = previous;

        public void Dispose() => _active.Value = _previous;
    }
}
