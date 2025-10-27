using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Jellyseerr TV Show that wraps TmdbTvResult with Jellyseerr-specific functionality.
/// 
/// This class provides:
/// - All TMDB TV properties through inheritance from TmdbTvResult
/// - Jellyseerr media info (download status, service IDs, etc.) via MediaInfo
/// - Equality comparison with Jellyfin Series items
/// 
/// This bridge model provides the complete structure returned by the discover TV API.
/// </summary>
public class JellyseerrShow 
    : TmdbTvResult, 
      IJellyseerrItem
{
    /// <summary>
    /// The library type for shows.
    /// </summary>
    [JsonIgnore]
    public static string LibraryType => "Shows";

    /// <summary>
    /// Jellyseerr-specific media information (download status, service IDs, etc.)
    /// </summary>
    [JsonPropertyName("mediaInfo")]
    public JellyseerrMedia? MediaInfo { get; set; }

    /// <summary>
    /// Override JSON property names to match camelCase format from discover API
    /// </summary>
    [JsonPropertyName("mediaType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public new JellyseerrModel.MediaType MediaType { get; set; }

    [JsonPropertyName("firstAirDate")]
    public new string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("genreIds")]
    public new List<int> GenreIds { get; set; } = new();

    [JsonPropertyName("originCountry")]
    public new List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalLanguage")]
    public new string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("originalName")]
    public new string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public new double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public new int VoteCount { get; set; }

    [JsonPropertyName("backdropPath")]
    public new string? BackdropPath { get; set; }

    [JsonPropertyName("posterPath")]
    public new string? PosterPath { get; set; }

    /// <summary>
    /// The media name for folder creation (Name from TMDB).
    /// </summary>
    [JsonPropertyName("_mediaName")]
    public string MediaName 
    { 
        get => Name;
        set => Name = value;
    }

    /// <summary>
    /// Computed property that extracts the year from the first air date.
    /// </summary>
    [JsonPropertyName("_year")]
    public string Year 
    { 
        get 
        {
            return IJellyseerrItem.ExtractYear(FirstAirDate);
        }
    }

    /// <summary>
    /// The extra external ID (TVDB for shows).
    /// </summary>
    [JsonPropertyName("extraId")]
    public string? ExtraId => MediaInfo?.TvdbId?.ToString();

    /// <summary>
    /// The display name for the extra external ID (tvdbid for shows).
    /// </summary>
    [JsonPropertyName("extraIdName")]
    public string ExtraIdName => "tvdbid";

    /// <summary>
    /// The creation date of the media item.
    /// </summary>
    [JsonPropertyName("createdDate")]
    public DateTimeOffset? CreatedDate { get; set; }

    /// <summary>
    /// The network tag for the media item.
    /// </summary>
    [JsonPropertyName("networkTag")]
    public string? NetworkTag { get; set; }

    /// <summary>
    /// The network ID for the media item.
    /// </summary>
    [JsonPropertyName("networkId")]
    public int? NetworkId { get; set; }

    /// <summary>
    /// Equality comparison with a Jellyfin Series item.
    /// </summary>
    public bool EqualsSeries(Series? other)
    {
        if (other is null) return false;
        
        // Use TMDB ID for comparison (Id from Jellyseerr discover endpoint is TMDB ID)
        var tmdbId = other.GetProviderId("Tmdb");
        if (!string.IsNullOrEmpty(tmdbId) && Id.ToString() == tmdbId)
        {
            return true;
        }
        
        // Fallback: Use TVDB ID for comparison if TMDB ID doesn't match
        var tvdbId = other.GetProviderId("Tvdb");
        if (!string.IsNullOrEmpty(tvdbId) && !string.IsNullOrEmpty(ExtraId) && tvdbId == ExtraId)
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin BaseItem (Series).
    /// </summary>
    public bool EqualsItem(BaseItem? other)
    {
        if (other is null) return false;
        
        // Only compare with Series items
        if (other is not Series series) return false;
        
        return EqualsSeries(series);
    }
    
    /// <summary>
    /// Returns the formatted string representation for automatic string conversion.
    /// </summary>
    public override string ToString()
    {
        return ((IJellyseerrItem)this).ToFolderString() ?? string.Empty;
    }
    
    /// <summary>
    /// Returns a hash code for the show that can be used for matching.
    /// </summary>
    public int GetItemHashCode()
    {
        return HashCode.Combine(Id, MediaName, Year, MediaType);
    }
    
    /// <summary>
    /// Generates XML content for the TV show in tvshow.nfo format.
    /// </summary>
    /// <returns>XML string for the TV show</returns>
    public string ToXmlString()
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<tvshow>");
        xml.AppendLine($"  <title>{Name}</title>");
        
        // Add TVDB ID if available
        if (!string.IsNullOrEmpty(ExtraId))
        {
            xml.AppendLine($"  <id>{ExtraId}</id>");
            xml.AppendLine($"  <uniqueid type=\"{ExtraIdName}\" default=\"true\">{ExtraId}</uniqueid>");
            xml.AppendLine($"  <tvdbid>{ExtraId}</tvdbid>");
            //<imdb_id>tt1305826</imdb_id>
            //<tvrageid>2638</tvrageid>
        }
        
        xml.AppendLine($"  <uniqueid type=\"tmdb\">{Id}</uniqueid>");
        xml.AppendLine($"  <tmdbid>{Id}</tmdbid>");
        
        // Add network tag if available
        if (!string.IsNullOrEmpty(NetworkTag))
        {
            xml.AppendLine($"  <tag>{NetworkTag}</tag>");
        }
        
        xml.AppendLine("</tvshow>");
        
        return xml.ToString();
    }
    
    /// <summary>
    /// Gets the NFO filename for the TV show.
    /// </summary>
    /// <returns>NFO filename string</returns>
    public static string GetNfoFilename()
    {
        return "tvshow.nfo"; // Static value for TV shows
    }
}
