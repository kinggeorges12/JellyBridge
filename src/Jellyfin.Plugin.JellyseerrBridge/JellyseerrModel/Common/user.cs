using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;





public enum UserType
{
    PLEX = 1,
    LOCAL = 2,
    JELLYFIN = 3,
    EMBY = 4
}

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
    public string? PlexUsername { get; set; } = null!;

    [JsonPropertyName("jellyfinUsername")]
    public string? JellyfinUsername { get; set; } = null!;

    [JsonPropertyName("username")]
    public string? Username { get; set; } = null!;

    [JsonPropertyName("password")]
    public string? Password { get; set; } = null!;

    [JsonPropertyName("resetPasswordGuid")]
    public string? ResetPasswordGuid { get; set; } = null!;

    [JsonPropertyName("recoveryLinkExpirationDate")]
    public string? RecoveryLinkExpirationDate { get; set; } = null!;

    [JsonPropertyName("userType")]
    public UserType UserType { get; set; } = new();

    [JsonPropertyName("plexId")]
    public string? PlexId { get; set; } = null!;

    [JsonPropertyName("jellyfinUserId")]
    public string? JellyfinUserId { get; set; } = null!;

    [JsonPropertyName("jellyfinDeviceId")]
    public string? JellyfinDeviceId { get; set; } = null!;

    [JsonPropertyName("jellyfinAuthToken")]
    public string? JellyfinAuthToken { get; set; } = null!;

    [JsonPropertyName("plexToken")]
    public string? PlexToken { get; set; } = null!;

    [JsonPropertyName("permissions")]
    public string Permissions { get; set; } = string.Empty;

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;

    [JsonPropertyName("avatarETag")]
    public string? AvatarETag { get; set; } = null!;

    [JsonPropertyName("avatarVersion")]
    public string? AvatarVersion { get; set; } = null!;

    [JsonPropertyName("requestCount")]
    public int RequestCount { get; set; }

    [JsonPropertyName("requests")]
    public List<MediaRequest> Requests { get; set; } = new();

    [JsonPropertyName("watchlists")]
    public List<Watchlist> Watchlists { get; set; } = new();

    [JsonPropertyName("movieQuotaLimit")]
    public int? MovieQuotaLimit { get; set; } = null!;

    [JsonPropertyName("movieQuotaDays")]
    public int? MovieQuotaDays { get; set; } = null!;

    [JsonPropertyName("tvQuotaLimit")]
    public int? TvQuotaLimit { get; set; } = null!;

    [JsonPropertyName("tvQuotaDays")]
    public int? TvQuotaDays { get; set; } = null!;

    // [JsonPropertyName("settings")]
    // public UserSettings? Settings { get; set; } // BLOCKED TYPE

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


