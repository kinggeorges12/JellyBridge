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
      IJellyseerrItem,
      IEquatable<JellyseerrMovie>, 
      IEquatable<Movie>
{
    /// <summary>
    /// Jellyseerr-specific media information (download status, service IDs, etc.)
    /// </summary>
    [JsonPropertyName("mediaInfo")]
    public JellyseerrMedia? MediaInfo { get; set; }

    /// <summary>
    /// Override JSON property names to match camelCase format from discover API
    /// </summary>
    [JsonPropertyName("mediaType")]
    public new string MediaType => base.MediaType;

    [JsonPropertyName("releaseDate")]
    public new string ReleaseDate => base.ReleaseDate;

    [JsonPropertyName("genreIds")]
    public new List<int> GenreIds => base.GenreIds;

    [JsonPropertyName("originalLanguage")]
    public new string OriginalLanguage => base.OriginalLanguage;

    [JsonPropertyName("originalTitle")]
    public new string OriginalTitle => base.OriginalTitle;

    [JsonPropertyName("voteAverage")]
    public new double VoteAverage => base.VoteAverage;

    [JsonPropertyName("voteCount")]
    public new int VoteCount => base.VoteCount;

    [JsonPropertyName("backdropPath")]
    public new string? BackdropPath => base.BackdropPath;

    [JsonPropertyName("posterPath")]
    public new string? PosterPath => base.PosterPath;

    /// <summary>
    /// The display name of the movie (Title from TMDB).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name => Title;

    /// <summary>
    /// Computed property that extracts the year from the release date.
    /// </summary>
    public string Year => IJellyseerrItem.ExtractYear(ReleaseDate);

    /// <summary>
    /// The extra external ID (IMDB for movies).
    /// </summary>
    public string? ExtraId => MediaInfo?.ImdbId;

    /// <summary>
    /// The display name for the extra external ID (imdbid for movies).
    /// </summary>
    public string ExtraIdName => "imdbid";

    /// <summary>
    /// Equality comparison with another JellyseerrMovie.
    /// </summary>
    public bool Equals(JellyseerrMovie? other)
    {
        if (other is null) return false;
        return Id == other.Id;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin Movie item.
    /// </summary>
    public bool Equals(Movie? other)
    {
        if (other is null) return false;
        
        // Use TMDB ID for comparison (Id from Jellyseerr discover endpoint is TMDB ID)
        if (!string.IsNullOrEmpty(other.GetProviderId("Tmdb")))
        {
            return Id.ToString() == other.GetProviderId("Tmdb");
        }
        
        return false;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin BaseItem (Movie).
    /// </summary>
    public bool Equals(BaseItem? other)
    {
        if (other is null) return false;
        
        // Only compare with Movie items
        if (other is not Movie movie) return false;
        
        return Equals(movie);
    }
}
