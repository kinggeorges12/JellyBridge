using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Test class for Jellyseerr Media objects with only the essential properties needed from the API.
/// This class contains only the properties that are actually used and avoids inheritance complexity.
/// </summary>
public class JellyseerrMediaTest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; }

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("status4k")]
    public int Status4k { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("lastSeasonChange")]
    public DateTimeOffset LastSeasonChange { get; set; }

    [JsonPropertyName("mediaAddedAt")]
    public DateTimeOffset? MediaAddedAt { get; set; }

    [JsonPropertyName("serviceId")]
    public int? ServiceId { get; set; }

    [JsonPropertyName("serviceId4k")]
    public int? ServiceId4k { get; set; }

    [JsonPropertyName("externalServiceId")]
    public int? ExternalServiceId { get; set; }

    [JsonPropertyName("externalServiceId4k")]
    public int? ExternalServiceId4k { get; set; }

    [JsonPropertyName("externalServiceSlug")]
    public string? ExternalServiceSlug { get; set; }

    [JsonPropertyName("externalServiceSlug4k")]
    public string? ExternalServiceSlug4k { get; set; }

    [JsonPropertyName("ratingKey")]
    public string? RatingKey { get; set; }

    [JsonPropertyName("ratingKey4k")]
    public string? RatingKey4k { get; set; }

    [JsonPropertyName("jellyfinMediaId")]
    public string? JellyfinMediaId { get; set; }

    [JsonPropertyName("jellyfinMediaId4k")]
    public string? JellyfinMediaId4k { get; set; }

    [JsonPropertyName("watchlists")]
    public List<object> Watchlists { get; set; } = new();

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; }

    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; }

    [JsonPropertyName("downloadStatus")]
    public List<object> DownloadStatus { get; set; } = new();

    [JsonPropertyName("downloadStatus4k")]
    public List<object> DownloadStatus4k { get; set; } = new();
}
