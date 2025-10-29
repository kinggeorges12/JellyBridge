using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using System;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

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
    /// Get the Jellyseerr media type for an IJellyfinItem.
    /// </summary>
    /// <param name="item">The IJellyfinItem to get the media type for</param>
    /// <returns>The Jellyseerr media type enum</returns>
    static JellyseerrModel.MediaType GetMediaType(IJellyfinItem item)
    {
        return item switch
        {
            JellyfinMovie => JellyseerrModel.MediaType.MOVIE,
            JellyfinSeries => JellyseerrModel.MediaType.TV,
            _ => throw new NotSupportedException($"Unsupported item type: {item.GetType().Name}")
        };
    }

    /// <summary>
    /// The TMDB ID of the media item.
    /// </summary>
    [JsonIgnore]
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
    [JsonIgnore]
    JellyseerrModel.MediaType MediaType { get; }

    /// <summary>
    /// Jellyseerr-specific media information (download status, service IDs, etc.).
    /// </summary>
    [JsonIgnore]
    JellyseerrMedia? MediaInfo { get; set; }

    /// <summary>
    /// The extra external ID (IMDB for movies, TVDB for shows).
    /// </summary>
    [JsonIgnore]
    string? ExtraId { get; }

    /// <summary>
    /// The display name for the extra external ID (e.g., "imdbid", "tvdbid").
    /// </summary>
    [JsonIgnore]
    string ExtraIdName { get; }

    /// <summary>
    /// The creation date of the media item.
    /// </summary>
    [JsonIgnore]
    DateTimeOffset? CreatedDate { get; set; }

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
        
        // Add extra ID name to identify the item as a movie or show in the folder name
        parts.Add($"[{ExtraIdName}]");
        
        return string.Join(" ", parts);
    }

    public abstract bool EqualsItem(IJellyfinItem? other);
    
    /// <summary>
    /// Returns a hash code for the item that can be used for matching.
    /// </summary>
    int GetItemHashCode();
    
    /// <summary>
    /// Generates XML content for the media item (movie.nfo or tvshow.nfo format).
    /// </summary>
    /// <returns>XML string for the media item</returns>
    string ToXmlString();
    
    /// <summary>
    /// Gets the NFO filename for the media item.
    /// </summary>
    /// <returns>NFO filename string</returns>
    static string GetNfoFilename() => throw new NotImplementedException();

    /// <summary>
    /// Gets the NFO filename for the media item.
    /// </summary>
    /// <param name="type">The type of the media item</param>
    /// <returns>NFO filename string</returns>
    static string GetNfoFilename(IJellyseerrItem item) => item switch
    {
        JellyseerrMovie => JellyseerrMovie.GetNfoFilename(),
        JellyseerrShow => JellyseerrShow.GetNfoFilename(),
        _ => throw new NotSupportedException($"Unsupported type: {item.GetType().Name}")
    };

    /// <summary>
    /// Gets the metadata filename for the media item.
    /// </summary>
    /// <returns>Metadata filename string</returns>
    static string GetMetadataFilename() => "metadata.json";
    
    /// <summary>
    /// The network tag for the media item.
    /// </summary>
    [JsonIgnore]
    string? NetworkTag { get; set; }
    
    /// <summary>
    /// The network tag for the media item.
    /// </summary>
    [JsonIgnore]
    int? NetworkId { get; set; }
}
