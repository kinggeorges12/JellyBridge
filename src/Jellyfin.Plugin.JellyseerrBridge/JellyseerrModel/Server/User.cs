using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class User
{
    [JsonPropertyName("filteredFields")]
    public List<string> FilteredFields { get; set; } = new();
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("plexUsername")]
    public string PlexUsername { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinUsername")]
    public string JellyfinUsername { get; set; } = string.Empty;
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
    [JsonPropertyName("resetPasswordGuid")]
    public string ResetPasswordGuid { get; set; } = string.Empty;
    [JsonPropertyName("recoveryLinkExpirationDate")]
    public string RecoveryLinkExpirationDate { get; set; } = string.Empty;
    [JsonPropertyName("userType")]
    public UserType UserType { get; set; } = new();
    [JsonPropertyName("plexId")]
    public string PlexId { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinDeviceId")]
    public string JellyfinDeviceId { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinAuthToken")]
    public string JellyfinAuthToken { get; set; } = string.Empty;
    [JsonPropertyName("plexToken")]
    public string PlexToken { get; set; } = string.Empty;
    [JsonPropertyName("permissions")]
    public string Permissions { get; set; } = string.Empty;
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;
    [JsonPropertyName("avatarETag")]
    public string AvatarETag { get; set; } = string.Empty;
    [JsonPropertyName("avatarVersion")]
    public string AvatarVersion { get; set; } = string.Empty;
    [JsonPropertyName("requestCount")]
    public int RequestCount { get; set; }
    [JsonPropertyName("requests")]
    public List<MediaRequest> Requests { get; set; } = new();
    [JsonPropertyName("watchlists")]
    public List<Watchlist> Watchlists { get; set; } = new();
    [JsonPropertyName("movieQuotaLimit")]
    public int MovieQuotaLimit { get; set; }
    [JsonPropertyName("movieQuotaDays")]
    public int MovieQuotaDays { get; set; }
    [JsonPropertyName("tvQuotaLimit")]
    public int TvQuotaLimit { get; set; }
    [JsonPropertyName("tvQuotaDays")]
    public int TvQuotaDays { get; set; }
    [JsonPropertyName("settings")]
    public UserSettings Settings { get; set; } = new();
    [JsonPropertyName("pushSubscriptions")]
    public List<UserPushSubscription> PushSubscriptions { get; set; } = new();
    [JsonPropertyName("createdIssues")]
    public List<Issue> CreatedIssues { get; set; } = new();
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}


