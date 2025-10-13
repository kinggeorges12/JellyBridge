using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr TV show that matches the actual discover tv API response.
/// 
/// SOURCE: This bridges the gap between TMDB TV show data and Jellyseerr-specific fields.
/// 
/// ACTUAL API RESPONSE: The discover/tv endpoint returns TMDB TV show data enhanced with
/// Jellyseerr-specific fields like media info, availability status, etc.
/// 
/// LOCATION: server/routes/discover.ts - /tv endpoint
///           server/models/Search.ts - mapTvResult() function
/// 
/// The response structure includes:
/// - TMDB TV show fields (id, name, overview, etc.)
/// - Jellyseerr media info (download status, service IDs, etc.)
/// - Additional fields like mediaInfo, availability, etc.
/// 
/// This bridge model provides the complete structure returned by the discover tv API.
/// </summary>
public class JellyseerrShow : TmdbTvResult, IJellyseerrItem, IEquatable<JellyseerrShow>, IEquatable<BaseItem>
{
    /// <summary>
    /// Gets the type of this Jellyseerr object.
    /// </summary>
    public string Type => "Show";

    // IJellyseerrItem interface implementations
    string? IJellyseerrItem.Name => Name;
    int IJellyseerrItem.Id => Id; // Id is now int from TmdbTvResult
    string? IJellyseerrItem.ImdbId => ExternalIds?.ImdbId;
    int? IJellyseerrItem.TvdbId => MediaInfo?.TvdbId;
    string? IJellyseerrItem.ReleaseDate => FirstAirDate; // Map FirstAirDate to ReleaseDate
    
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
    /// Computed property that extracts the year from the first air date.
    /// </summary>
    public string Year => ExtractYear(FirstAirDate);

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
    /// Equality comparison with another JellyseerrShow.
    /// </summary>
    public bool Equals(JellyseerrShow? other)
    {
        if (other is null) return false;
        return Id == other.Id;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin BaseItem (Series).
    /// </summary>
    public bool Equals(BaseItem? other)
    {
        if (other is null) return false;
        
        // Only compare with Series items
        if (other is not Series series) return false;
        
        // Use TMDB ID for comparison (Id from Jellyseerr discover endpoint is TMDB ID)
        if (!string.IsNullOrEmpty(((BaseItem)series).GetProviderId("Tmdb")))
        {
            return Id.ToString() == ((BaseItem)series).GetProviderId("Tmdb");
        }
        
        return false;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as JellyseerrShow);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
