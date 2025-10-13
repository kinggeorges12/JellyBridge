using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

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
/// This bridge model inherits from the generated User model and overrides
/// specific properties to match the actual API response structure.
/// </summary>
public class JellyseerrUser : User
{
    // Override properties that have different types in the API response
    
    [JsonPropertyName("userType")]
    public new int UserType { get; set; }
    
    [JsonPropertyName("permissions")]
    public new int Permissions { get; set; }
    
    [JsonPropertyName("movieQuotaLimit")]
    public new int? MovieQuotaLimit { get; set; }
    
    [JsonPropertyName("tvQuotaLimit")]
    public new int? TvQuotaLimit { get; set; }
    
    [JsonPropertyName("tvQuotaDays")]
    public new int? TvQuotaDays { get; set; }
    
    [JsonPropertyName("createdAt")]
    public new string? CreatedAt { get; set; }
    
    [JsonPropertyName("updatedAt")]
    public new string? UpdatedAt { get; set; }
    
    [JsonPropertyName("settings")]
    public object? Settings { get; set; } // Keep as object to avoid circular references
}