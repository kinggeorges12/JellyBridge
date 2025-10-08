using System.ComponentModel.DataAnnotations;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

/// <summary>
/// Represents a network name to ID mapping for XML serialization compatibility.
/// </summary>
public class NetworkMapping
{
    /// <summary>
    /// Gets or sets the network name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the network ID.
    /// </summary>
    public int Id { get; set; }
}

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
    /// Gets or sets the list of active/default networks.
    /// </summary>
    public List<string> ActiveNetworks { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the mapping of network names to their IDs (populated after API communication).
    /// This is stored as a list of key-value pairs for XML serialization compatibility.
    /// </summary>
    public List<NetworkMapping> NetworkNameToId { get; set; } = new List<NetworkMapping>();

    /// <summary>
    /// Gets the default networks list as a newline-separated string (for backward compatibility).
    /// This is a computed property that doesn't get serialized to avoid conflicts.
    /// </summary>
    [System.Xml.Serialization.XmlIgnore]
    public string DefaultNetworks => string.Join("\n", ActiveNetworks);

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

    /// <summary>
    /// Gets the network name to ID mapping as a dictionary for easier access.
    /// </summary>
    public Dictionary<string, int> GetNetworkNameToIdDictionary()
    {
        return NetworkNameToId.ToDictionary(m => m.Name, m => m.Id);
    }

    /// <summary>
    /// Sets the network name to ID mapping from a dictionary.
    /// </summary>
    /// <param name="mapping">The dictionary mapping network names to IDs.</param>
    public void SetNetworkNameToIdDictionary(Dictionary<string, int> mapping)
    {
        NetworkNameToId = mapping.Select(kvp => new NetworkMapping { Name = kvp.Key, Id = kvp.Value }).ToList();
    }

    /// <summary>
    /// Ensures that ActiveNetworks is initialized with default networks if it's empty.
    /// </summary>
    public void EnsureDefaultNetworks()
    {
        if (!ActiveNetworks.Any())
        {
            ActiveNetworks = new List<string>(JellyseerrDefaultNetworks);
        }
    }
}
