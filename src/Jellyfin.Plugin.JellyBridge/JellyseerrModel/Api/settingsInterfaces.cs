using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;

public class LogsResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<LogMessage> Results { get; set; } = new();

}

public class SettingsAboutResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("totalRequests")]
    public int TotalRequests { get; set; }

    [JsonPropertyName("totalMediaItems")]
    public int TotalMediaItems { get; set; }

    [JsonPropertyName("tz")]
    public string? Tz { get; set; } = null!;

    [JsonPropertyName("appDataPath")]
    public string AppDataPath { get; set; } = string.Empty;

}

public class PublicSettingsResponse
{
    [JsonPropertyName("jellyfinHost")]
    public string? JellyfinHost { get; set; } = null!;

    [JsonPropertyName("jellyfinExternalHost")]
    public string? JellyfinExternalHost { get; set; } = null!;

    [JsonPropertyName("jellyfinServerName")]
    public string? JellyfinServerName { get; set; } = null!;

    [JsonPropertyName("jellyfinForgotPasswordUrl")]
    public string? JellyfinForgotPasswordUrl { get; set; } = null!;

    [JsonPropertyName("initialized")]
    public bool Initialized { get; set; }

    [JsonPropertyName("applicationTitle")]
    public string ApplicationTitle { get; set; } = string.Empty;

    [JsonPropertyName("applicationUrl")]
    public string ApplicationUrl { get; set; } = string.Empty;

    [JsonPropertyName("hideAvailable")]
    public bool HideAvailable { get; set; }

    [JsonPropertyName("hideBlacklisted")]
    public bool HideBlacklisted { get; set; }

    [JsonPropertyName("localLogin")]
    public bool LocalLogin { get; set; }

    [JsonPropertyName("mediaServerLogin")]
    public bool MediaServerLogin { get; set; }

    [JsonPropertyName("movie4kEnabled")]
    public bool Movie4kEnabled { get; set; }

    [JsonPropertyName("series4kEnabled")]
    public bool Series4kEnabled { get; set; }

    [JsonPropertyName("discoverRegion")]
    public string DiscoverRegion { get; set; } = string.Empty;

    [JsonPropertyName("streamingRegion")]
    public string StreamingRegion { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("mediaServerType")]
    public int MediaServerType { get; set; }

    [JsonPropertyName("partialRequestsEnabled")]
    public bool PartialRequestsEnabled { get; set; }

    [JsonPropertyName("enableSpecialEpisodes")]
    public bool EnableSpecialEpisodes { get; set; }

    [JsonPropertyName("cacheImages")]
    public bool CacheImages { get; set; }

    [JsonPropertyName("vapidPublic")]
    public string VapidPublic { get; set; } = string.Empty;

    [JsonPropertyName("enablePushRegistration")]
    public bool EnablePushRegistration { get; set; }

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("emailEnabled")]
    public bool EmailEnabled { get; set; }

    [JsonPropertyName("newPlexLogin")]
    public bool NewPlexLogin { get; set; }

    [JsonPropertyName("youtubeUrl")]
    public string YoutubeUrl { get; set; } = string.Empty;

}

public class CacheItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("stats")]
    public CacheItemStats Stats { get; set; } = new();

}

public class StatusResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("commitTag")]
    public string CommitTag { get; set; } = string.Empty;

    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("commitsBehind")]
    public int CommitsBehind { get; set; }

    [JsonPropertyName("restartRequired")]
    public bool RestartRequired { get; set; }

}

public class LogMessage
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; set; } = null!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    // TypeScript: Record<string, unknown>
    public Dictionary<string, object>? Data { get; set; } = null!;

}



public class CacheItemStats
{
    [JsonPropertyName("hits")]
    public int Hits { get; set; }

    [JsonPropertyName("misses")]
    public int Misses { get; set; }

    [JsonPropertyName("keys")]
    public int Keys { get; set; }

    [JsonPropertyName("ksize")]
    public int Ksize { get; set; }

    [JsonPropertyName("vsize")]
    public int Vsize { get; set; }

}