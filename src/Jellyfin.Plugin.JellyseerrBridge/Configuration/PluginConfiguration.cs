using System.ComponentModel.DataAnnotations;
using MediaBrowser.Model.Plugins;
using Jellyfin.Plugin.JellyseerrBridge.Utils;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

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
        { nameof(MaxDiscoverPages), 1 },
        { nameof(MaxCollectionDays), 30 },
        { nameof(RequestTimeout), 30 },
        { nameof(RetryAttempts), 3 },
        { nameof(EnableDebugLogging), false },
        { nameof(PlaceholderDurationSeconds), 10 },
        { nameof(Region), "US" },
        { nameof(ManageJellyseerrLibrary), true },
        { nameof(NetworkMap), new List<JellyseerrNetwork>
            { // Network Names and IDs in comments
                new JellyseerrNetwork { Country = "US", Name = "Netflix", Id = 8, DisplayPriority = 4 }, // Netflix: 213
                new JellyseerrNetwork { Country = "US", Name = "Disney Plus", Id = 337, DisplayPriority = 1 }, // Disney+: 2739
                new JellyseerrNetwork { Country = "US", Name = "Amazon Prime Video", Id = 9, DisplayPriority = 3 }, // Prime Video: 1024
                new JellyseerrNetwork { Country = "US", Name = "Apple TV+", Id = 350, DisplayPriority = 8 }, // Apple TV+: 2552
                new JellyseerrNetwork { Country = "US", Name = "Hulu", Id = 15, DisplayPriority = 7 }, // Hulu: 453
                new JellyseerrNetwork { Country = "US", Name = "HBO Max", Id = 49, DisplayPriority = 27 }, // HBO: 49
                new JellyseerrNetwork { Country = "US", Name = "Discovery +", Id = 520, DisplayPriority = 163 }, // Discovery+: 4353
                new JellyseerrNetwork { Country = "US", Name = "ABC", Id = 148, DisplayPriority = 255 }, // ABC: 2
                new JellyseerrNetwork { Country = "US", Name = "FOX", Id = 328, DisplayPriority = 97 }, // FOX: 19
                new JellyseerrNetwork { Country = "US", Name = "Cinemax Amazon Channel", Id = 289, DisplayPriority = 72 }, // Cinemax: 359
                new JellyseerrNetwork { Country = "US", Name = "AMC", Id = 80, DisplayPriority = 47 }, // AMC: 174
                new JellyseerrNetwork { Country = "US", Name = "Paramount+ with Showtime", Id = 1770, DisplayPriority = 19 }, // Showtime: 67
                new JellyseerrNetwork { Country = "US", Name = "Starz", Id = 43, DisplayPriority = 40 }, // Starz: 318
                new JellyseerrNetwork { Country = "US", Name = "The CW", Id = 83, DisplayPriority = 35 }, // The CW: 71
                new JellyseerrNetwork { Country = "US", Name = "NBC", Id = 79, DisplayPriority = 51 }, // NBC: 6    
                //new JellyseerrNetwork { Name = "CBS", Id = 16 }, // Not available on show providers
                new JellyseerrNetwork { Country = "US", Name = "Paramount Plus", Id = 531, DisplayPriority = 6 }, // Paramount+: 4330
                new JellyseerrNetwork { Country = "GB", Name = "BBC iPlayer", Id = 38, DisplayPriority = 12 }, // BBC One: 4
                new JellyseerrNetwork { Country = "US", Name = "Cartoon Network Amazon Channel", Id = 2329, DisplayPriority = 240 }, // Cartoon Network: 56
                new JellyseerrNetwork { Country = "US", Name = "Adult Swim", Id = 318, DisplayPriority = 95 }, // Adult Swim: 80
                //new JellyseerrNetwork { Name = "Nickelodeon", Id = 13 }, // Not available on show providers
                new JellyseerrNetwork { Country = "US", Name = "Peacock Premium Plus", Id = 387, DisplayPriority = 219 }, // Peacock: 3353
            }
        }
    };

    /// <summary>
    /// Gets or sets the Jellyseerr base URL.
    /// </summary>
    [Required]
    public string JellyseerrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library directory.
    /// </summary>
    [Required]
    public string LibraryDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user ID for requests.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Gets or sets whether the plugin is enabled.
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the sync interval in hours.
    /// </summary>
    public double? SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets whether to create separate libraries for streaming services.
    /// </summary>
    public bool? CreateSeparateLibraries { get; set; }

    /// <summary>
    /// Gets or sets the prefix for streaming service libraries.
    /// </summary>
    public string LibraryPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to exclude placeholder shows from main libraries.
    /// </summary>
    public bool? ExcludeFromMainLibraries { get; set; }

    /// <summary>
    /// Gets or sets whether to auto-sync on startup.
    /// </summary>
    public bool? AutoSyncOnStartup { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages to fetch from discover endpoint for each network during sync (0 = unlimited).
    /// This applies to both movies and TV shows discovery.
    /// </summary>
    public int? MaxDiscoverPages { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of days to keep items in the collection before cleanup.
    /// Items older than this will be removed during sync operations.
    /// </summary>
    public int? MaxCollectionDays { get; set; }

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int? RequestTimeout { get; set; }

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int? RetryAttempts { get; set; }

    /// <summary>
    /// Gets or sets whether to enable debug logging.
    /// </summary>
    public bool? EnableDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets the default duration (in seconds) for generated placeholder videos.
    /// </summary>
    public int? PlaceholderDurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the watch network region (ISO 3166-1 country code).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mapping of network IDs to their names (populated after API communication).
    /// This is stored as a list of key-value pairs for XML serialization compatibility.
    /// </summary>
    public List<JellyseerrNetwork> NetworkMap { get; set; } = new List<JellyseerrNetwork>((List<JellyseerrNetwork>)DefaultValues[nameof(NetworkMap)]);

    /// <summary>
    /// Gets or sets whether to manage libraries with JellyseerrBridge.
    /// </summary>
    public bool? ManageJellyseerrLibrary { get; set; }

    /// <summary>
    /// Returns a JSON representation of the configuration with API key masked.
    /// </summary>
    public override string ToString()
    {
        var properties = GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(this));
        
        // Mask the API key
        if (properties.ContainsKey(nameof(ApiKey)))
        {
            properties[nameof(ApiKey)] = string.IsNullOrEmpty(ApiKey) ? "[EMPTY]" : "[SET]";
        }
        
        return JellyseerrJsonSerializer.Serialize(properties);
    }
}
