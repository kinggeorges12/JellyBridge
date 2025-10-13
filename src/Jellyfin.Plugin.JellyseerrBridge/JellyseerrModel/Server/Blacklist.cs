using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class Blacklist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; } = new();
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }
    [JsonPropertyName("user")]
    public User User { get; set; } = new();
    [JsonPropertyName("media")]
    public Media Media { get; set; } = new();
    [JsonPropertyName("blacklistedTags")]
    public string BlacklistedTags { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}


