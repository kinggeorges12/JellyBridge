using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class RequestResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<object> Results { get; set; } = new();

}

public class MediaRequestBody
{
    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; } = new();

    [JsonPropertyName("mediaId")]
    public int MediaId { get; set; }

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null!;

    [JsonPropertyName("seasons")]
    public object? Seasons { get; set; } = null!;

    [JsonPropertyName("is4k")]
    public bool? Is4k { get; set; } = null!;

    [JsonPropertyName("serverId")]
    public int? ServerId { get; set; } = null!;

    [JsonPropertyName("profileId")]
    public int? ProfileId { get; set; } = null!;

    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("rootFolder")]
    public string? RootFolder { get; set; } = string.Empty;

    [JsonPropertyName("languageProfileId")]
    public int? LanguageProfileId { get; set; } = null!;

    [JsonPropertyName("userId")]
    public int? UserId { get; set; } = null!;

    [JsonPropertyName("tags")]
    public List<int>? Tags { get; set; } = new();

}

