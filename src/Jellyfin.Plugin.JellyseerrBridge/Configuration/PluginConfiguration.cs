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
        /// Gets or sets the maximum number of pages to fetch per network during sync (0 = unlimited).
        /// </summary>
        public int MaxPagesPerNetwork { get; set; } = 10;

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
    /// Gets the default network name to ID mappings based on Jellyseerr's NetworkSlider configuration.
    /// </summary>
    public static List<NetworkMapping> JellyseerrDefaultNetworkMappings => new List<NetworkMapping>
    {
        new NetworkMapping { Name = "Netflix", Id = 213 },
        new NetworkMapping { Name = "Disney+", Id = 2739 },
        new NetworkMapping { Name = "Prime Video", Id = 1024 },
        new NetworkMapping { Name = "Apple TV+", Id = 2552 },
        new NetworkMapping { Name = "Hulu", Id = 453 },
        new NetworkMapping { Name = "HBO", Id = 49 },
        new NetworkMapping { Name = "Discovery+", Id = 4353 },
        new NetworkMapping { Name = "ABC", Id = 2 },
        new NetworkMapping { Name = "FOX", Id = 19 },
        new NetworkMapping { Name = "Cinemax", Id = 359 },
        new NetworkMapping { Name = "AMC", Id = 174 },
        new NetworkMapping { Name = "Showtime", Id = 67 },
        new NetworkMapping { Name = "Starz", Id = 318 },
        new NetworkMapping { Name = "The CW", Id = 71 },
        new NetworkMapping { Name = "NBC", Id = 6 },
        new NetworkMapping { Name = "CBS", Id = 16 },
        new NetworkMapping { Name = "Paramount+", Id = 4330 },
        new NetworkMapping { Name = "BBC One", Id = 4 },
        new NetworkMapping { Name = "Cartoon Network", Id = 56 },
        new NetworkMapping { Name = "Adult Swim", Id = 80 },
        new NetworkMapping { Name = "Nickelodeon", Id = 13 },
        new NetworkMapping { Name = "Peacock", Id = 3353 }
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

    /// <summary>
    /// Ensures that NetworkNameToId is initialized with default network mappings if it's empty.
    /// </summary>
    public void EnsureDefaultNetworkMappings()
    {
        if (!NetworkNameToId.Any())
        {
            NetworkNameToId = new List<NetworkMapping>(JellyseerrDefaultNetworkMappings);
        }
    }

    /// <summary>
    /// Gets the default value for a configuration property.
    /// </summary>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The default value for the property.</returns>
    public static object GetDefaultValue(string propertyName)
    {
        return propertyName switch
        {
            nameof(JellyseerrUrl) => "http://localhost:5055",
            nameof(ApiKey) => string.Empty,
            nameof(LibraryDirectory) => "/data/Jellyseerr",
            nameof(UserId) => 1,
            nameof(IsEnabled) => true,
            nameof(SyncIntervalHours) => 24,
            nameof(CreateSeparateLibraries) => false,
            nameof(LibraryPrefix) => "Streaming - ",
            nameof(ExcludeFromMainLibraries) => true,
            nameof(AutoSyncOnStartup) => false,
            nameof(MaxPagesPerNetwork) => 10,
            nameof(RequestTimeout) => 30,
            nameof(RetryAttempts) => 3,
            nameof(EnableDebugLogging) => false,
            nameof(WatchProviderRegion) => "US",
            _ => throw new ArgumentException($"Unknown property: {propertyName}")
        };
    }
}
