using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using CommonMediaType = Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.MediaType;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr Media objects that customizes the generated Media model
/// to match the actual API response structure from the TypeScript Media entity.
/// Only overrides properties where the type or JSON property name changes.
/// </summary>
public class JellyseerrMedia : Media
{
    // Properties that need type changes from base class

    /// <summary>
    /// Media type as enum - explicitly use Common MediaType to avoid conflicts
    /// </summary>
    [JsonPropertyName("mediaType")]
    public new CommonMediaType MediaType { get; set; }

    /// <summary>
    /// Download status as array (TypeScript: downloadStatus?: DownloadingItem[] = [])
    /// Override: Remove nullable from base class
    /// </summary>
    [JsonPropertyName("downloadStatus")]
    public new List<DownloadingItem> DownloadStatus { get; set; } = new();

    /// <summary>
    /// Download status for 4K as array (TypeScript: downloadStatus4k?: DownloadingItem[] = [])
    /// Override: Remove nullable from base class
    /// </summary>

    /// <summary>
    /// Watchlists as array (TypeScript: watchlists: null | Watchlist[])
    /// Override: Change from string to List<Watchlist>
    /// </summary>
    [JsonPropertyName("watchlists")]
    public new List<Watchlist> Watchlists { get; set; } = new();

    /// <summary>
    /// Service ID as nullable number (TypeScript: serviceId?: number | null)
    /// Override: Change from string? to int?
    /// </summary>
    [JsonIgnore]
    public new string? ServiceId { get; set; }

    [JsonPropertyName("serviceId")]
    public int? ServiceIdInt { get; set; }

    /// <summary>
    /// Service ID for 4K as nullable number (TypeScript: serviceId4k?: number | null)
    /// Override: Change from string? to int? and ignore base class property
    /// </summary>
    [JsonIgnore]
    public new string? ServiceId4k { get; set; }

    [JsonPropertyName("serviceId4k")]
    public int? ServiceId4kInt { get; set; }

    /// <summary>
    /// External service ID as nullable number (TypeScript: externalServiceId?: number | null)
    /// Override: Change from string? to int? and ignore base class property
    /// </summary>
    [JsonIgnore]
    public new string? ExternalServiceId { get; set; }

    [JsonPropertyName("externalServiceId")]
    public int? ExternalServiceIdInt { get; set; }

    /// <summary>
    /// External service ID for 4K as nullable number (TypeScript: externalServiceId4k?: number | null)
    /// Override: Change from string? to int? and ignore base class property
    /// </summary>
    [JsonIgnore]
    public new string? ExternalServiceId4k { get; set; }

    [JsonPropertyName("externalServiceId4k")]
    public int? ExternalServiceId4kInt { get; set; }

    // Properties that are not used in Jellyseerr API responses - exclude from JSON

    /// <summary>
    /// Requests - Not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new List<MediaRequest> Requests { get; set; } = new();

    /// <summary>
    /// Seasons - Not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new List<Season> Seasons { get; set; } = new();

    /// <summary>
    /// Issues - Not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new List<Issue> Issues { get; set; } = new();

    /// <summary>
    /// Blacklist - Not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new Blacklist? Blacklist { get; set; }

    /// <summary>
    /// Service URL for 4K - Not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new string? ServiceUrl4k { get; set; }

    /// <summary>
    /// Media URL for 4K - Not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new string? MediaUrl4k { get; set; }

    /// <summary>
    /// iOS Plex URL - Plex-specific, not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new string? IOSPlexUrl { get; set; }

    /// <summary>
    /// iOS Plex URL for 4K - Plex-specific, not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new string? IOSPlexUrl4k { get; set; }

    /// <summary>
    /// Tautulli URL - Tautulli-specific, not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new string? TautulliUrl { get; set; }

    /// <summary>
    /// Tautulli URL for 4K - Tautulli-specific, not used in Jellyseerr API responses
    /// </summary>
    [JsonIgnore]
    public new string? TautulliUrl4k { get; set; }

    // All other properties (Id, MediaType, TmdbId, TvdbId, ImdbId, Status, Status4k, 
    // CreatedAt, UpdatedAt, LastSeasonChange, MediaAddedAt, ExternalServiceSlug, 
    // ExternalServiceSlug4k, RatingKey, RatingKey4k, JellyfinMediaId, JellyfinMediaId4k, 
    // MediaUrl, ServiceUrl) inherit from base class with correct types and JSON names
    // Note: ServiceId, ServiceId4k, ExternalServiceId, ExternalServiceId4k are overridden 
    // as ServiceIdInt, ServiceId4kInt, ExternalServiceIdInt, ExternalServiceId4kInt 
    // to handle numeric JSON values instead of string values from base class
}
