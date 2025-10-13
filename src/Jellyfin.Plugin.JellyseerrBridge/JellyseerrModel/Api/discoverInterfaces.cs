using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class GenreSliderItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("backdrops")]
    public List<string> Backdrops { get; set; } = new();

}

public class WatchlistItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("ratingKey")]
    public string RatingKey { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

}

public class WatchlistResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("results")]
    public List<WatchlistItem> Results { get; set; } = new();

}

