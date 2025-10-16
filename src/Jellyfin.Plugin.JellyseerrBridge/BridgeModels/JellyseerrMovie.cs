using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Jellyseerr Movie that wraps TmdbMovieResult with Jellyseerr-specific functionality.
/// 
/// This class provides:
/// - All TMDB movie properties through inheritance from TmdbMovieResult
/// - Jellyseerr media info (download status, service IDs, etc.) via MediaInfo
/// - Equality comparison with Jellyfin Movie items
/// 
/// This bridge model provides the complete structure returned by the discover movies API.
/// </summary>
public class JellyseerrMovie 
    : TmdbMovieResult, 
      IJellyseerrItem
{
    /// <summary>
    /// The library type for movies.
    /// </summary>
    [JsonIgnore]
    public static string LibraryType => "Movies";

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

    [JsonPropertyName("releaseDate")]
    public new string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("genreIds")]
    public new List<int> GenreIds { get; set; } = new();

    [JsonPropertyName("originalLanguage")]
    public new string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public new string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public new double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public new int VoteCount { get; set; }

    [JsonPropertyName("backdropPath")]
    public new string? BackdropPath { get; set; }

    [JsonPropertyName("posterPath")]
    public new string? PosterPath { get; set; }

    /// <summary>
    /// The media name for folder creation (Title from TMDB).
    /// </summary>
    [JsonPropertyName("_mediaName")]
    public string MediaName 
    { 
        get => Title;
        set => Title = value;
    }

    /// <summary>
    /// Computed property that extracts the year from the release date.
    /// </summary>
    [JsonPropertyName("_year")]
    public string Year 
    { 
        get 
        {
            return IJellyseerrItem.ExtractYear(ReleaseDate);
        }
    }

    /// <summary>
    /// The extra external ID (IMDB for movies).
    /// </summary>
    [JsonIgnore]
    public string? ExtraId => MediaInfo?.ImdbId;

    /// <summary>
    /// The display name for the extra external ID (imdbid for movies).
    /// </summary>
    [JsonIgnore]
    public string ExtraIdName => "imdbid";

    /// <summary>
    /// Equality comparison with a Jellyfin Movie item.
    /// </summary>
    public bool EqualsMovie(Movie? other)
    {
        if (other is null) return false;
        
        // Use TMDB ID for comparison (Id from Jellyseerr discover endpoint is TMDB ID)
        var tmdbId = other.GetProviderId("Tmdb");
        if (!string.IsNullOrEmpty(tmdbId) && Id.ToString() == tmdbId)
        {
            return true;
        }
        
        // Fallback: Use IMDB ID for comparison if TMDB ID doesn't match
        var imdbId = other.GetProviderId("Imdb");
        if (!string.IsNullOrEmpty(imdbId) && !string.IsNullOrEmpty(ExtraId) && imdbId == ExtraId)
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin BaseItem (Movie).
    /// </summary>
    public bool EqualsItem(BaseItem? other)
    {
        if (other is null) return false;
        
        // Only compare with Movie items
        if (other is not Movie movie) return false;
        
        return EqualsMovie(movie);
    }
    
    /// <summary>
    /// Returns the formatted string representation for automatic string conversion.
    /// </summary>
    public override string ToString()
    {
        return ((IJellyseerrItem)this).ToFolderString() ?? string.Empty;
    }
}
