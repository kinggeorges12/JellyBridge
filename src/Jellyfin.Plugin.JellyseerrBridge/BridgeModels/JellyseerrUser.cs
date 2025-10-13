using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr user that inherits from the full User entity and filters sensitive data.
/// 
/// SOURCE: This inherits from JellyseerrModel.Server.User and overrides sensitive fields
/// to match the actual API response structure.
/// 
/// ACTUAL API RESPONSE: The API returns filtered user data via user.filter() method
/// which excludes sensitive fields like passwords, tokens, etc.
/// 
/// LOCATION: server/routes/user/index.ts - user.filter() method
/// 
/// This bridge model inherits all properties from the full User model but:
/// - Makes sensitive fields nullable or empty (passwords, tokens, etc.)
/// - Provides a clean interface for API responses
/// - Maintains type safety while filtering sensitive data
/// </summary>
public class JellyseerrUser : User
{
    // Override sensitive fields to be nullable/empty for API responses
    [JsonPropertyName("password")]
    public new string? Password { get; set; } = null;
    
    [JsonPropertyName("resetPasswordGuid")]
    public new string? ResetPasswordGuid { get; set; } = null;
    
    [JsonPropertyName("recoveryLinkExpirationDate")]
    public new string? RecoveryLinkExpirationDate { get; set; } = null;
    
    [JsonPropertyName("plexToken")]
    public new string? PlexToken { get; set; } = null;
    
    [JsonPropertyName("jellyfinAuthToken")]
    public new string? JellyfinAuthToken { get; set; } = null;
    
    [JsonPropertyName("jellyfinDeviceId")]
    public new string? JellyfinDeviceId { get; set; } = null;
    
    // Override complex relationships to be nullable for API responses
    [JsonPropertyName("requests")]
    public new List<MediaRequest>? Requests { get; set; } = null;
    
    [JsonPropertyName("watchlists")]
    public new List<Watchlist>? Watchlists { get; set; } = null;
    
    [JsonPropertyName("settings")]
    public new UserSettings? Settings { get; set; } = null;
    
    [JsonPropertyName("pushSubscriptions")]
    public new List<UserPushSubscription>? PushSubscriptions { get; set; } = null;
    
    [JsonPropertyName("createdIssues")]
    public new List<Issue>? CreatedIssues { get; set; } = null;
    
    [JsonPropertyName("warnings")]
    public new List<string>? Warnings { get; set; } = null;
    
    [JsonPropertyName("filteredFields")]
    public new List<string>? FilteredFields { get; set; } = null;
    
    // Override quota fields to be nullable for API responses
    [JsonPropertyName("movieQuotaLimit")]
    public new int? MovieQuotaLimit { get; set; } = null;
    
    [JsonPropertyName("movieQuotaDays")]
    public new int? MovieQuotaDays { get; set; } = null;
    
    [JsonPropertyName("tvQuotaLimit")]
    public new int? TvQuotaLimit { get; set; } = null;
    
    [JsonPropertyName("tvQuotaDays")]
    public new int? TvQuotaDays { get; set; } = null;
    
    // Override avatar-related fields to be nullable for API responses
    [JsonPropertyName("avatarETag")]
    public new string? AvatarETag { get; set; } = null;
    
    [JsonPropertyName("avatarVersion")]
    public new string? AvatarVersion { get; set; } = null;
    
    // Override request count to be nullable for API responses
    [JsonPropertyName("requestCount")]
    public new int? RequestCount { get; set; } = null;
}

