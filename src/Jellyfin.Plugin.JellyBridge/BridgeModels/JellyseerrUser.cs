using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr user that matches the actual API response structure.
/// 
/// SOURCE: This is based on the actual API response from /api/v1/auth/me endpoint
/// which returns filtered user data via user.filter() method.
/// 
/// ACTUAL API RESPONSE: The API returns a simple JSON object with specific fields,
/// not the full User entity with all relationships.
/// 
/// LOCATION: server/routes/user/index.ts - user.filter() method
/// 
/// This bridge model inherits from the generated User model and handles type mismatches.
/// </summary>
public class JellyseerrUser : User
{
    // Override properties that have different types in the API response
    
    [JsonPropertyName("permissions")]
    public new int Permissions { get; set; }
    
    // API returns string dates, but base User class expects DateTimeOffset
    [JsonPropertyName("createdAt")]
    public new string? CreatedAt { get; set; }
    
    // API returns string dates, but base User class expects DateTimeOffset
    [JsonPropertyName("updatedAt")]
    public new string? UpdatedAt { get; set; }
    
    [JsonPropertyName("settings")]
    public object? Settings { get; set; } // Keep as object to avoid circular references
    
    /// <summary>
    /// Helper property that converts the JellyfinUserId string to a proper GUID format.
    /// Jellyseerr stores Jellyfin user IDs without hyphens, but Jellyfin uses standard GUID format with hyphens.
    /// </summary>
    [JsonIgnore]
    public string? JellyfinUserGuid => 
        !string.IsNullOrEmpty(JellyfinUserId) && Guid.TryParse(JellyfinUserId, out var guid) 
            ? guid.ToString() 
            : null;
}