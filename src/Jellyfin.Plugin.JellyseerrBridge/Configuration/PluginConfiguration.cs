using System.ComponentModel.DataAnnotations;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

/// <summary>
/// Represents a network ID to name mapping for XML serialization compatibility.
/// </summary>
public class NetworkEntry
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
    /// Dictionary containing default values for all configuration properties.
    /// </summary>
    public static readonly Dictionary<string, object> DefaultValues = new()
    {
        { nameof(JellyseerrUrl), "http://localhost:5055" },
        { nameof(ApiKey), string.Empty },
        { nameof(LibraryDirectory), "/data/Jellyseerr" },
        { nameof(UserId), 1 },
        { nameof(IsEnabled), true },
        { nameof(SyncIntervalHours), 24 },
        { nameof(CreateSeparateLibraries), false },
        { nameof(LibraryPrefix), "Streaming - " },
        { nameof(ExcludeFromMainLibraries), true },
        { nameof(AutoSyncOnStartup), false },
        { nameof(MaxDiscoverPages), 10 },
        { nameof(RequestTimeout), 30 },
        { nameof(RetryAttempts), 3 },
        { nameof(EnableDebugLogging), false },
        { nameof(Region), "US" },
        { "DefaultNetworkMap", new List<NetworkEntry>
            {
                new NetworkEntry { Name = "Netflix", Id = 213 },
                new NetworkEntry { Name = "Disney+", Id = 2739 },
                new NetworkEntry { Name = "Prime Video", Id = 1024 },
                new NetworkEntry { Name = "Apple TV+", Id = 2552 },
                new NetworkEntry { Name = "Hulu", Id = 453 },
                new NetworkEntry { Name = "HBO", Id = 49 },
                new NetworkEntry { Name = "Discovery+", Id = 4353 },
                new NetworkEntry { Name = "ABC", Id = 2 },
                new NetworkEntry { Name = "FOX", Id = 19 },
                new NetworkEntry { Name = "Cinemax", Id = 359 },
                new NetworkEntry { Name = "AMC", Id = 174 },
                new NetworkEntry { Name = "Showtime", Id = 67 },
                new NetworkEntry { Name = "Starz", Id = 318 },
                new NetworkEntry { Name = "The CW", Id = 71 },
                new NetworkEntry { Name = "NBC", Id = 6 },
                new NetworkEntry { Name = "CBS", Id = 16 },
                new NetworkEntry { Name = "Paramount+", Id = 4330 },
                new NetworkEntry { Name = "BBC One", Id = 4 },
                new NetworkEntry { Name = "Cartoon Network", Id = 56 },
                new NetworkEntry { Name = "Adult Swim", Id = 80 },
                new NetworkEntry { Name = "Nickelodeon", Id = 13 },
                new NetworkEntry { Name = "Peacock", Id = 3353 }
            }
        }
    };

    /// <summary>
    /// Gets or sets the Jellyseerr base URL.
    /// </summary>
    [Required]
    public string JellyseerrUrl { get; set; } = (string)DefaultValues[nameof(JellyseerrUrl)];

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = (string)DefaultValues[nameof(ApiKey)];

    /// <summary>
    /// Gets or sets the library directory.
    /// </summary>
    [Required]
    public string LibraryDirectory { get; set; } = (string)DefaultValues[nameof(LibraryDirectory)];

    /// <summary>
    /// Gets or sets the user ID for requests.
    /// </summary>
    public int UserId { get; set; } = (int)DefaultValues[nameof(UserId)];

    /// <summary>
    /// Gets or sets whether the plugin is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = (bool)DefaultValues[nameof(IsEnabled)];

    /// <summary>
    /// Gets or sets the sync interval in hours.
    /// </summary>
    public int SyncIntervalHours { get; set; } = (int)DefaultValues[nameof(SyncIntervalHours)];

    /// <summary>
    /// Gets or sets whether to create separate libraries for streaming services.
    /// </summary>
    public bool CreateSeparateLibraries { get; set; } = (bool)DefaultValues[nameof(CreateSeparateLibraries)];

    /// <summary>
    /// Gets or sets the prefix for streaming service libraries.
    /// </summary>
    public string LibraryPrefix { get; set; } = (string)DefaultValues[nameof(LibraryPrefix)];

    /// <summary>
    /// Gets or sets whether to exclude placeholder shows from main libraries.
    /// </summary>
    public bool ExcludeFromMainLibraries { get; set; } = (bool)DefaultValues[nameof(ExcludeFromMainLibraries)];

    /// <summary>
    /// Gets or sets whether to auto-sync on startup.
    /// </summary>
    public bool AutoSyncOnStartup { get; set; } = (bool)DefaultValues[nameof(AutoSyncOnStartup)];

    /// <summary>
    /// Gets or sets the maximum number of pages to fetch from discover endpoint for each network during sync (0 = unlimited).
    /// This applies to both movies and TV shows discovery.
    /// </summary>
    public int MaxDiscoverPages { get; set; } = (int)DefaultValues[nameof(MaxDiscoverPages)];

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int RequestTimeout { get; set; } = (int)DefaultValues[nameof(RequestTimeout)];

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int RetryAttempts { get; set; } = (int)DefaultValues[nameof(RetryAttempts)];

    /// <summary>
    /// Gets or sets whether to enable debug logging.
    /// </summary>
    public bool EnableDebugLogging { get; set; } = (bool)DefaultValues[nameof(EnableDebugLogging)];

    /// <summary>
    /// Gets or sets the watch network region (ISO 3166-1 country code).
    /// </summary>
    public string Region { get; set; } = (string)DefaultValues[nameof(Region)];

    /// <summary>
    /// Gets or sets the mapping of network IDs to their names (populated after API communication).
    /// This is stored as a list of key-value pairs for XML serialization compatibility.
    /// </summary>
    public List<NetworkEntry> NetworkMap { get; set; } = new List<NetworkEntry>((List<NetworkEntry>)DefaultValues["DefaultNetworkMap"]);




    /// <summary>
    /// Gets the network ID to name mapping as a dictionary for easier access.
    /// </summary>
    public Dictionary<int, string> GetNetworkMapDictionary()
    {
        return NetworkMap
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>
    /// Sets the network ID to name mapping from a dictionary.
    /// </summary>
    /// <param name="mapping">The dictionary mapping network IDs to names.</param>
    public void SetNetworkMapDictionary(Dictionary<int, string> mapping)
    {
        NetworkMap = mapping.Select(kvp => new NetworkEntry { Name = kvp.Value, Id = kvp.Key }).ToList();
    }
}
