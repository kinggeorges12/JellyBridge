using System.ComponentModel.DataAnnotations;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Jellyseerr base URL.
    /// </summary>
    [Required]
    public string JellyseerrUrl { get; set; } = "http://localhost:5055";

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library directory.
    /// </summary>
    [Required]
    public string LibraryDirectory { get; set; } = "/data/Jellyseerr";

    /// <summary>
    /// Gets or sets the user ID for requests.
    /// </summary>
    public int UserId { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether the plugin is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the sync interval in hours.
    /// </summary>
    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets whether to create separate libraries for streaming services.
    /// </summary>
    public bool CreateSeparateLibraries { get; set; } = false;

    /// <summary>
    /// Gets or sets the prefix for streaming service libraries.
    /// </summary>
    public string LibraryPrefix { get; set; } = "Streaming - ";

    /// <summary>
    /// Gets or sets whether to exclude placeholder shows from main libraries.
    /// </summary>
    public bool ExcludeFromMainLibraries { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to auto-sync on startup.
    /// </summary>
    public bool AutoSyncOnStartup { get; set; } = false;

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int RequestTimeout { get; set; } = 30;

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether to enable debug logging.
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Gets or sets the watch provider region (ISO 3166-1 country code).
    /// </summary>
    public string WatchProviderRegion { get; set; } = "US";

    /// <summary>
    /// Gets the default list of networks/streaming services.
    /// </summary>
    public static List<string> JellyseerrDefaultNetworks => new List<string>
    {
        "Netflix",
        "Disney+",
        "Prime Video",
        "Apple TV+",
        "Hulu",
        "HBO",
        "Discovery+",
        "ABC",
        "FOX",
        "Cinemax",
        "AMC",
        "Showtime",
        "Starz",
        "The CW",
        "NBC",
        "CBS",
        "Paramount+",
        "BBC One",
        "Cartoon Network",
        "Adult Swim",
        "Nickelodeon",
        "Peacock"
    };
}
