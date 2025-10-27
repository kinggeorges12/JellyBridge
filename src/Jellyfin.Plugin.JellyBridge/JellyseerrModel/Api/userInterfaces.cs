using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;

public class UserResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<User> Results { get; set; } = new();

}

public class UserRequestsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<MediaRequest> Results { get; set; } = new();

}

public class QuotaStatus
{
    [JsonPropertyName("days")]
    public int? Days { get; set; } = null!;

    [JsonPropertyName("limit")]
    public int? Limit { get; set; } = null!;

    [JsonPropertyName("used")]
    public int Used { get; set; }

    [JsonPropertyName("remaining")]
    public int? Remaining { get; set; } = null!;

    [JsonPropertyName("restricted")]
    public bool Restricted { get; set; }

}

public class QuotaResponse
{
    [JsonPropertyName("movie")]
    public QuotaStatus Movie { get; set; } = new();

    [JsonPropertyName("tv")]
    public QuotaStatus Tv { get; set; } = new();

}

public class UserWatchDataResponse
{
    [JsonPropertyName("recentlyWatched")]
    public List<Media> RecentlyWatched { get; set; } = new();

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

}

