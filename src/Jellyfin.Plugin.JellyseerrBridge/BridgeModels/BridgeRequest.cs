using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.Models;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge Jellyseerr request model for bridge functionality.
/// </summary>
public class BridgeRequest
{
    /// <summary>
    /// Gets or sets the request ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the request status.
    /// </summary>
    public MediaRequestStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the request status as an integer (for compatibility).
    /// </summary>
    [JsonIgnore]
    public int StatusInt => (int)Status;

    /// <summary>
    /// Gets or sets when the request was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the request was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the request type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a 4K request.
    /// </summary>
    public bool Is4k { get; set; }

    /// <summary>
    /// Gets or sets the server ID.
    /// </summary>
    public int? ServerId { get; set; }

    /// <summary>
    /// Gets or sets the profile ID.
    /// </summary>
    public int? ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the root folder path.
    /// </summary>
    public string? RootFolder { get; set; }

    /// <summary>
    /// Gets or sets the language profile ID.
    /// </summary>
    public int? LanguageProfileId { get; set; }

    /// <summary>
    /// Gets or sets the tags for this request.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Gets or sets whether this is an auto request.
    /// </summary>
    public bool IsAutoRequest { get; set; }

    /// <summary>
    /// Gets or sets the media information for this request.
    /// </summary>
    public BridgeRequestMedia? Media { get; set; }

    /// <summary>
    /// Gets or sets the seasons for this request.
    /// </summary>
    public List<BridgeRequestSeason>? Seasons { get; set; }

    /// <summary>
    /// Gets or sets the user who modified this request.
    /// </summary>
    public BridgeUser? ModifiedBy { get; set; }

    /// <summary>
    /// Gets or sets the user who requested this.
    /// </summary>
    public BridgeUser? RequestedBy { get; set; }

    /// <summary>
    /// Gets or sets the season count.
    /// </summary>
    public int SeasonCount { get; set; }

    /// <summary>
    /// Gets or sets whether this request can be removed.
    /// </summary>
    public bool CanRemove { get; set; }
}

/// <summary>
/// Extended Jellyseerr request season model.
/// </summary>
public class BridgeRequestSeason
{
    /// <summary>
    /// Gets or sets the season ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the season status.
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Gets or sets when the season was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the season was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Extended Jellyseerr request media model.
/// </summary>
public class BridgeRequestMedia : Media
{
    /// <summary>
    /// Gets or sets the download status.
    /// </summary>
    public List<object> DownloadStatus { get; set; } = new();

    /// <summary>
    /// Gets or sets the 4K download status.
    /// </summary>
    public List<object> DownloadStatus4k { get; set; } = new();

    /// <summary>
    /// Gets or sets the external service slug.
    /// </summary>
    public string ExternalServiceSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the 4K external service slug.
    /// </summary>
    public string? ExternalServiceSlug4k { get; set; }

    /// <summary>
    /// Gets or sets the rating key.
    /// </summary>
    public string? RatingKey { get; set; }

    /// <summary>
    /// Gets or sets the 4K rating key.
    /// </summary>
    public string? RatingKey4k { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin media ID.
    /// </summary>
    public string? JellyfinMediaId { get; set; }

    /// <summary>
    /// Gets or sets the 4K Jellyfin media ID.
    /// </summary>
    public string? JellyfinMediaId4k { get; set; }

    /// <summary>
    /// Gets or sets the media URL.
    /// </summary>
    public string? MediaUrl { get; set; }

    /// <summary>
    /// Gets or sets the service URL.
    /// </summary>
    public string? ServiceUrl { get; set; }
}

/// <summary>
/// Extended Jellyseerr user model.
/// </summary>
public class BridgeUser
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the user email.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user avatar.
    /// </summary>
    public string? Avatar { get; set; }

    /// <summary>
    /// Gets or sets whether the user is a system user.
    /// </summary>
    public bool IsSystemUser { get; set; }
}
