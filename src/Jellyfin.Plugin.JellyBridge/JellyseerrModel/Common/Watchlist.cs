using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel;

public class DuplicateWatchlistRequestError : Exception
{
}

public class NotFoundError : Exception
{
}



public class Watchlist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("ratingKey")]
    public string RatingKey { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; } = new();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("requestedBy")]
    public User RequestedBy { get; set; } = new();

    [JsonPropertyName("media")]
    public Media Media { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

}



