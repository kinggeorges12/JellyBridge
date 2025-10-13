using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr media info that inherits from the full Media entity and filters unnecessary data.
/// 
/// SOURCE: This inherits from JellyseerrModel.Server.Media and overrides complex fields
/// to match the actual API response structure used in discover responses.
/// 
/// ACTUAL API RESPONSE: The discover endpoints return media information that includes
/// both TMDB data and Jellyseerr-specific fields like download status, service IDs, etc.
/// 
/// LOCATION: server/routes/discover.ts - mapMovieResult() and mapTvResult() functions
///           server/models/Search.ts - mapMovieResult() and mapTvResult() implementations
/// 
/// This bridge model inherits all properties from the full Media model but:
/// - Makes complex relationships nullable (requests, seasons, issues, etc.)
/// - Provides a clean interface for discover API responses
/// - Maintains type safety while filtering unnecessary data
/// </summary>
public class JellyseerrMediaInfo : Media
{
    // Override complex relationships to be nullable for API responses
    [JsonPropertyName("requests")]
    public new List<MediaRequest>? Requests { get; set; } = null;
    
    [JsonPropertyName("seasons")]
    public new List<Season>? Seasons { get; set; } = null;
    
    [JsonPropertyName("issues")]
    public new List<Issue>? Issues { get; set; } = null;
    
    [JsonPropertyName("blacklist")]
    public new Blacklist? Blacklist { get; set; } = null;
    
    // Override service-related fields to be nullable for API responses
    [JsonPropertyName("serviceId")]
    public new string? ServiceId { get; set; } = null;
    
    [JsonPropertyName("serviceId4k")]
    public new string? ServiceId4k { get; set; } = null;
    
    [JsonPropertyName("externalServiceId")]
    public new string? ExternalServiceId { get; set; } = null;
    
    [JsonPropertyName("externalServiceId4k")]
    public new string? ExternalServiceId4k { get; set; } = null;
    
    [JsonPropertyName("externalServiceSlug")]
    public new string? ExternalServiceSlug { get; set; } = null;
    
    [JsonPropertyName("externalServiceSlug4k")]
    public new string? ExternalServiceSlug4k { get; set; } = null;
    
    [JsonPropertyName("ratingKey")]
    public new string? RatingKey { get; set; } = null;
    
    [JsonPropertyName("ratingKey4k")]
    public new string? RatingKey4k { get; set; } = null;
    
    [JsonPropertyName("jellyfinMediaId")]
    public new string? JellyfinMediaId { get; set; } = null;
    
    [JsonPropertyName("jellyfinMediaId4k")]
    public new string? JellyfinMediaId4k { get; set; } = null;
    
    [JsonPropertyName("serviceUrl")]
    public new string? ServiceUrl { get; set; } = null;
    
    [JsonPropertyName("serviceUrl4k")]
    public new string? ServiceUrl4k { get; set; } = null;
    
    [JsonPropertyName("mediaUrl")]
    public new string? MediaUrl { get; set; } = null;
    
    [JsonPropertyName("mediaUrl4k")]
    public new string? MediaUrl4k { get; set; } = null;
    
    [JsonPropertyName("iOSPlexUrl")]
    public new string? IOSPlexUrl { get; set; } = null;
    
    [JsonPropertyName("iOSPlexUrl4k")]
    public new string? IOSPlexUrl4k { get; set; } = null;
    
    [JsonPropertyName("tautulliUrl")]
    public new string? TautulliUrl { get; set; } = null;
    
    [JsonPropertyName("tautulliUrl4k")]
    public new string? TautulliUrl4k { get; set; } = null;
    
    // Override date fields to be nullable for API responses
    [JsonPropertyName("lastSeasonChange")]
    public new DateTimeOffset? LastSeasonChange { get; set; } = null;
    
    [JsonPropertyName("mediaAddedAt")]
    public new DateTimeOffset? MediaAddedAt { get; set; } = null;
    
    // Override watchlists to be nullable for API responses
    [JsonPropertyName("watchlists")]
    public new string? Watchlists { get; set; } = null;
}