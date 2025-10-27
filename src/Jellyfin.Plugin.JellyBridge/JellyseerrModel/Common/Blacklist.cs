using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel;



public class Blacklist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; } = new();

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null!;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("user")]
    public User? User { get; set; } = null!;

    [JsonPropertyName("media")]
    public Media Media { get; set; } = new();

    [JsonPropertyName("blacklistedTags")]
    public string? BlacklistedTags { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

}


