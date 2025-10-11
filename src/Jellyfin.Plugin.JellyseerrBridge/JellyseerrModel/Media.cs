using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Media
/// </summary>
public class Media
{

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; } = 0;

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; } = null;

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null;

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null;

    [JsonPropertyName("status")]
    public int Status { get; set; } = 0;

    [JsonPropertyName("status4k")]
    public int Status4k { get; set; } = 0;

    [JsonPropertyName("requests")]
    public List<string> Requests { get; set; } = new();

    [JsonPropertyName("watchlists")]
    public string? Watchlists { get; set; } = null;

    [JsonPropertyName("seasons")]
    public List<Season> Seasons { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();

    [JsonPropertyName("blacklist")]
    public string? Blacklist { get; set; } = null;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("lastSeasonChange")]
    public DateTime LastSeasonChange { get; set; }

    [JsonPropertyName("mediaAddedAt")]
    public DateTime MediaAddedAt { get; set; }

    [JsonPropertyName("serviceId")]
    public int? ServiceId { get; set; } = null;

    [JsonPropertyName("serviceId4k")]
    public int? ServiceId4k { get; set; } = null;

    [JsonPropertyName("externalServiceId")]
    public int? ExternalServiceId { get; set; } = null;

    [JsonPropertyName("externalServiceId4k")]
    public int? ExternalServiceId4k { get; set; } = null;

    [JsonPropertyName("externalServiceSlug")]
    public string? ExternalServiceSlug { get; set; } = null;

    [JsonPropertyName("externalServiceSlug4k")]
    public string? ExternalServiceSlug4k { get; set; } = null;

    [JsonPropertyName("ratingKey")]
    public string? RatingKey { get; set; } = null;

    [JsonPropertyName("ratingKey4k")]
    public string? RatingKey4k { get; set; } = null;

    [JsonPropertyName("jellyfinMediaId")]
    public string? JellyfinMediaId { get; set; } = null;

    [JsonPropertyName("jellyfinMediaId4k")]
    public string? JellyfinMediaId4k { get; set; } = null;

    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; } = null;

    [JsonPropertyName("serviceUrl4k")]
    public string? ServiceUrl4k { get; set; } = null;

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; } = null;

    [JsonPropertyName("mediaUrl4k")]
    public string? MediaUrl4k { get; set; } = null;

    [JsonPropertyName("iOSPlexUrl")]
    public string? IOSPlexUrl { get; set; } = null;

    [JsonPropertyName("iOSPlexUrl4k")]
    public string? IOSPlexUrl4k { get; set; } = null;

    [JsonPropertyName("tautulliUrl")]
    public string? TautulliUrl { get; set; } = null;

    [JsonPropertyName("tautulliUrl4k")]
    public string? TautulliUrl4k { get; set; } = null;

}
