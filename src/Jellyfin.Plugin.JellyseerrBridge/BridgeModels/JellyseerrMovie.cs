using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr movie that inherits from TmdbMovieResult and adds Jellyseerr-specific fields.
/// 
/// SOURCE: This inherits from JellyseerrModel.TmdbMovieResult and adds Jellyseerr-specific fields
/// like media info, availability status, etc.
/// 
/// ACTUAL API RESPONSE: The discover/movies endpoint returns TMDB movie data enhanced with
/// Jellyseerr-specific fields like media info, availability status, etc.
/// 
/// LOCATION: server/routes/discover.ts - /movies endpoint
///           server/models/Search.ts - mapMovieResult() function
/// 
/// This bridge model inherits all TMDB movie properties and adds:
/// - Jellyseerr media info (download status, service IDs, etc.)
/// - Additional fields like mediaInfo, availability, etc.
/// 
/// The response structure includes:
/// - TMDB movie fields (inherited from TmdbMovieResult)
/// - Jellyseerr media info (download status, service IDs, etc.)
/// - Additional fields like mediaInfo, availability, etc.
/// 
/// This bridge model provides the complete structure returned by the discover movies API.
/// </summary>
public class JellyseerrMovie : TmdbMovieResult, IJellyseerrItem, IEquatable<JellyseerrMovie>, IEquatable<BaseItem>
{
    /// <summary>
    /// Gets the type of this Jellyseerr object.
    /// </summary>
    public string Type => "Movie";

    // IJellyseerrItem interface implementations
    string? IJellyseerrItem.Name => Title;
    string? IJellyseerrItem.ImdbId => ExternalIds?.ImdbId;
    int? IJellyseerrItem.TvdbId => MediaInfo?.TvdbId;
    
    // Jellyseerr-specific fields
    [JsonPropertyName("mediaInfo")]
    public JellyseerrMediaInfo? MediaInfo { get; set; }
    
    [JsonPropertyName("availability")]
    public string? Availability { get; set; }
    
    [JsonPropertyName("in4k")]
    public bool In4k { get; set; }
    
    [JsonPropertyName("requestStatus")]
    public string? RequestStatus { get; set; }
    
    [JsonPropertyName("requestStatus4k")]
    public string? RequestStatus4k { get; set; }
    
    [JsonPropertyName("canRequest")]
    public bool CanRequest { get; set; }
    
    [JsonPropertyName("canRequest4k")]
    public bool CanRequest4k { get; set; }
    
    [JsonPropertyName("externalIds")]
    public ExternalIds? ExternalIds { get; set; }
    
    [JsonPropertyName("genres")]
    public List<JellyseerrModel.Server.Genre>? Genres { get; set; }
    
    [JsonPropertyName("watchlists")]
    public List<WatchlistItem> Watchlists { get; set; } = new();
    
    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; }
    
    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Computed property that extracts the year from the release date.
    /// </summary>
    public string Year => ExtractYear(ReleaseDate);

    /// <summary>
    /// Extract year from date string.
    /// </summary>
    private static string ExtractYear(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return string.Empty;
            
        if (DateTime.TryParse(dateString, out var date))
            return date.Year.ToString();
            
        return string.Empty;
    }

    /// <summary>
    /// Equality comparison with another JellyseerrMovie.
    /// </summary>
    public bool Equals(JellyseerrMovie? other)
    {
        if (other is null) return false;
        return Id == other.Id;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin BaseItem (Movie).
    /// </summary>
    public bool Equals(BaseItem? other)
    {
        if (other is null) return false;
        
        // Only compare with Movie items
        if (other is not Movie movie) return false;
        
        // Use TMDB ID for comparison (Id from Jellyseerr discover endpoint is TMDB ID)
        if (!string.IsNullOrEmpty(((BaseItem)movie).GetProviderId("Tmdb")))
        {
            return Id.ToString() == ((BaseItem)movie).GetProviderId("Tmdb");
        }
        
        return false;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as JellyseerrMovie);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
