using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class MediaResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<Media> Results { get; set; } = new();

}

public class MediaWatchDataResponse
{
    [JsonPropertyName("data")]
    public MediaWatchDataResponseData? Data { get; set; } = null!;

    [JsonPropertyName("data4k")]
    public MediaWatchDataResponseData4k? Data4k { get; set; } = null!;

}



public class MediaWatchDataResponseData
{
    [JsonPropertyName("users")]
    public List<User> Users { get; set; } = new();

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("playCount7Days")]
    public int PlayCount7Days { get; set; }

    [JsonPropertyName("playCount30Days")]
    public int PlayCount30Days { get; set; }

}

public class MediaWatchDataResponseData4k
{
    [JsonPropertyName("users")]
    public List<User> Users { get; set; } = new();

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("playCount7Days")]
    public int PlayCount7Days { get; set; }

    [JsonPropertyName("playCount30Days")]
    public int PlayCount30Days { get; set; }

}