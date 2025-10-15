using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;





public enum MediaRequestStatus
{
    PENDING = 1,
    APPROVED,
    DECLINED,
    FAILED,
    COMPLETED
}

public enum MediaType
{
    [JsonPropertyName("movie")]
    MOVIE = 0,
    [JsonPropertyName("tv")]
    TV = 1
}

public enum MediaStatus
{
    UNKNOWN = 1,
    PENDING,
    PROCESSING,
    PARTIALLY_AVAILABLE,
    AVAILABLE,
    BLACKLISTED,
    DELETED
}

public class Media
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; } = new();

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null!;

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("status")]
    public MediaStatus Status { get; set; } = new();

    [JsonPropertyName("status4k")]
    public MediaStatus Status4k { get; set; } = new();

    [JsonPropertyName("requests")]
    public List<MediaRequest> Requests { get; set; } = new();

    [JsonPropertyName("watchlists")]
    public string Watchlists { get; set; } = string.Empty;

    [JsonPropertyName("seasons")]
    public List<Season> Seasons { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<Issue> Issues { get; set; } = new();

    [JsonPropertyName("blacklist")]
    public Blacklist? Blacklist { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("lastSeasonChange")]
    public DateTimeOffset LastSeasonChange { get; set; }

    [JsonPropertyName("mediaAddedAt")]
    public DateTimeOffset? MediaAddedAt { get; set; } = null!;

    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; } = null!;

    [JsonPropertyName("serviceId4k")]
    public string? ServiceId4k { get; set; } = null!;

    [JsonPropertyName("externalServiceId")]
    public string? ExternalServiceId { get; set; } = null!;

    [JsonPropertyName("externalServiceId4k")]
    public string? ExternalServiceId4k { get; set; } = null!;

    [JsonPropertyName("externalServiceSlug")]
    public string? ExternalServiceSlug { get; set; } = null!;

    [JsonPropertyName("externalServiceSlug4k")]
    public string? ExternalServiceSlug4k { get; set; } = null!;

    [JsonPropertyName("ratingKey")]
    public string? RatingKey { get; set; } = null!;

    [JsonPropertyName("ratingKey4k")]
    public string? RatingKey4k { get; set; } = null!;

    [JsonPropertyName("jellyfinMediaId")]
    public string? JellyfinMediaId { get; set; } = null!;

    [JsonPropertyName("jellyfinMediaId4k")]
    public string? JellyfinMediaId4k { get; set; } = null!;

    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; } = null!;

    [JsonPropertyName("serviceUrl4k")]
    public string? ServiceUrl4k { get; set; } = null!;

    [JsonPropertyName("downloadStatus")]
    public List<DownloadingItem>? DownloadStatus { get; set; } = new();

    [JsonPropertyName("downloadStatus4k")]
    public List<DownloadingItem>? DownloadStatus4k { get; set; } = new();

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; } = null!;

    [JsonPropertyName("mediaUrl4k")]
    public string? MediaUrl4k { get; set; } = null!;

    [JsonPropertyName("iOSPlexUrl")]
    public string? IOSPlexUrl { get; set; } = null!;

    [JsonPropertyName("iOSPlexUrl4k")]
    public string? IOSPlexUrl4k { get; set; } = null!;

    [JsonPropertyName("tautulliUrl")]
    public string? TautulliUrl { get; set; } = null!;

    [JsonPropertyName("tautulliUrl4k")]
    public string? TautulliUrl4k { get; set; } = null!;

}


