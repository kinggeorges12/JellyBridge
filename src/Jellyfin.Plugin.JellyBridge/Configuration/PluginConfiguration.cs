using System.ComponentModel.DataAnnotations;
using MediaBrowser.Model.Plugins;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.BridgeModels;

namespace Jellyfin.Plugin.JellyBridge.Configuration;

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
        // General
        { nameof(IsEnabled), false },
        { nameof(JellyseerrUrl), "http://localhost:5055" },
        { nameof(ApiKey), string.Empty },
        { nameof(SyncIntervalHours), 24.0 },
        { nameof(EnableStartupSync), true },
        { nameof(StartupDelaySeconds), 30 },

        // Library Settings
        { nameof(LibraryDirectory), "/data/JellyBridge" },
        { nameof(ExcludeFromMainLibraries), true },
        { nameof(RemoveRequestedFromFavorites), false },
        { nameof(CreateSeparateLibraries), false },
        { nameof(LibraryPrefix), string.Empty },
        { nameof(ManageJellyseerrLibrary), true },

        // Discover / Sync Settings
        { nameof(Region), "US" },
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
        },
        { nameof(MaxDiscoverPages), 1 },

        // Advanced Settings
        { nameof(RequestTimeout), 60 },
        { nameof(RetryAttempts), 3 },
        { nameof(MaxRetentionDays), 30 },
        { nameof(PlaceholderDurationSeconds), 10 },
        { nameof(EnableDebugLogging), false },
        { nameof(EnableTraceLogging), false },

        // Internal flags
        // { nameof(RanFirstTime), false }
    };

    // ===== General =====
    /// <summary>
    /// Gets or sets whether the plugin is enabled.
    /// </summary>
    public bool? IsEnabled { get; set; }

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
    /// Gets or sets the sync interval in hours.
    /// </summary>
    public double? SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets whether to auto-sync on startup.
    /// </summary>
    public bool? EnableStartupSync { get; set; }

    /// <summary>
    /// Gets or sets the delay in seconds before running the auto-sync on startup task.
    /// </summary>
    public int? StartupDelaySeconds { get; set; }

    // ===== Library Settings =====
    /// <summary>
    /// Gets or sets the library directory.
    /// </summary>
    [Required]
    public string LibraryDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to exclude placeholder shows from main libraries.
    /// </summary>
    public bool? ExcludeFromMainLibraries { get; set; }

    /// <summary>
    /// When enabled, remove items from all users' favorites after creating a request in Jellyseerr.
    /// </summary>
    public bool? RemoveRequestedFromFavorites { get; set; }

    /// <summary>
    /// Gets or sets whether to create separate libraries for streaming services.
    /// </summary>
    public bool? CreateSeparateLibraries { get; set; }

    /// <summary>
    /// Gets or sets the prefix for streaming service libraries.
    /// </summary>
    public string LibraryPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to manage libraries with JellyBridge.
    /// </summary>
    public bool? ManageJellyseerrLibrary { get; set; }

    // ===== Discover / Sync Settings =====
    /// <summary>
    /// Gets or sets the watch network region (ISO 3166-1 country code).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mapping of network IDs to their names (populated after API communication).
    /// Nullable to distinguish between "no value saved yet" and an empty list.
    /// </summary>
    public List<JellyseerrNetwork>? NetworkMap { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages to fetch from discover endpoint for each network during sync (0 = unlimited).
    /// This applies to both movies and TV shows discovery.
    /// </summary>
    public int? MaxDiscoverPages { get; set; }

    // ===== Advanced Settings =====
    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int? RequestTimeout { get; set; }

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int? RetryAttempts { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of days to retain items in the collection before cleanup.
    /// Items older than this will be removed during sync operations.
    /// </summary>
    public int? MaxRetentionDays { get; set; }

    /// <summary>
    /// Gets or sets the default duration (in seconds) for generated placeholder videos.
    /// </summary>
    public int? PlaceholderDurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets whether to enable debug logging.
    /// </summary>
    public bool? EnableDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets whether to enable trace logging.
    /// </summary>
    public bool? EnableTraceLogging { get; set; }

    // ===== Internal =====
    /// <summary>
    /// Gets or sets whether the plugin has run for the first time (determines if full library refresh is needed).
    /// </summary>
    // public bool? RanFirstTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when scheduled task triggers were last updated due to config change.
    /// Used to calculate next run time when triggers are reloaded.
    /// </summary>
    public DateTimeOffset? ScheduledTaskTimestamp { get; set; }

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
        
        return JellyBridgeJsonSerializer.Serialize(properties);
    }
}
