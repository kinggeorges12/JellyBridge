using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using System;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Interface for Jellyseerr media items that provides common functionality.
/// </summary>
public interface IJellyseerrItem
{
    /// <summary>
    /// The library type for the media item.
    /// </summary>
    [JsonIgnore]
    static virtual string LibraryType => throw new NotImplementedException();

    /// <summary>
    /// The TMDB ID of the media item.
    /// </summary>
    int Id { get; }

    /// <summary>
    /// The media name for folder creation (separate from Name to avoid conflicts).
    /// </summary>
    [JsonPropertyName("_mediaName")]
    string MediaName { get; set; }

    /// <summary>
    /// The year of the media item.
    /// </summary>
    [JsonPropertyName("_year")]
    string Year { get; }

    /// <summary>
    /// The media type from the API response (inherited from base classes).
    /// </summary>
    [JsonPropertyName("mediaType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    JellyseerrModel.MediaType MediaType { get; }

    /// <summary>
    /// Jellyseerr-specific media information (download status, service IDs, etc.).
    /// </summary>
    JellyseerrMedia? MediaInfo { get; set; }

    /// <summary>
    /// The extra external ID (IMDB for movies, TVDB for shows).
    /// </summary>
    string? ExtraId { get; }

    /// <summary>
    /// The display name for the extra external ID (e.g., "imdbid", "tvdbid").
    /// </summary>
    string ExtraIdName { get; }

    /// <summary>
    /// The creation date of the media item.
    /// </summary>
    [JsonPropertyName("createdDate")]
    virtual DateTimeOffset? CreatedDate => DateTimeOffset.Now;

    /// <summary>
    /// Extract year from date string.
    /// </summary>
    static string ExtractYear(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return string.Empty;
            
        if (DateTime.TryParse(dateString, out var date))
            return date.Year.ToString();
            
        return string.Empty;
    }

    /// <summary>
    /// Returns a formatted string representation suitable for folder naming.
    /// Format: "MediaName (Year) [tmdbid-ID] [extraidname-ExtraId]" with dynamic sections based on available values.
    /// </summary>
    string? ToFolderString()
    {
        var parts = new List<string>();
        
        // Always include MediaName
        parts.Add(MediaName);
        
        // Add year if available
        if (!string.IsNullOrEmpty(Year))
        {
            parts.Add($"({Year})");
        }
        
        // Always include TMDB ID
        parts.Add($"[tmdbid-{Id}]");
        
        //TODO: Testing
        // Add extra ID if available
        if (!string.IsNullOrEmpty(ExtraId))
        {
            parts.Add($"[{ExtraIdName}-{ExtraId}]");
        } else {
            parts.Add($"[{ExtraIdName}-]");
        }
        
        return string.Join(" ", parts);
    }

    public abstract bool EqualsItem(BaseItem? other);
    
    /// <summary>
    /// Returns a hash code for the item that can be used for matching.
    /// </summary>
    int GetItemHashCode();
}
