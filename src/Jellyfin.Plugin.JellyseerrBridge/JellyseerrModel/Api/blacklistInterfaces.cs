using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class BlacklistItem
{
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; } = null!;

    [JsonPropertyName("user")]
    public User? User { get; set; } = null!;

    [JsonPropertyName("blacklistedTags")]
    public string? BlacklistedTags { get; set; } = null!;

}

public enum MediaType
{
    Movie = 0,
    Tv
}

public class BlacklistResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<BlacklistItem> Results { get; set; } = new();

}


