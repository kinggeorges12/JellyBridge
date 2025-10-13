using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class BlacklistItem
{
    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; } = null!;

    [JsonPropertyName("user")]
    public User? User { get; set; } = null!;

    [JsonPropertyName("blacklistedTags")]
    public string? BlacklistedTags { get; set; } = string.Empty;

}

public class BlacklistResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<BlacklistItem> Results { get; set; } = new();

}


