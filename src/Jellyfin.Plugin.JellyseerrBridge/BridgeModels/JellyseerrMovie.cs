using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
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
/// - All TMDB movie properties through inheritance
/// - Jellyseerr media info (download status, service IDs, etc.)
/// - Equality comparison with Jellyfin Movie items
/// 
/// This bridge model provides the complete structure returned by the discover movies API.
/// </summary>
public class JellyseerrMovie 
    : TmdbMovieResult, 
      IJellyseerrMedia,
      IEquatable<JellyseerrMovie>, 
      IEquatable<Movie>
{
    /// <summary>
    /// The display name of the movie (Title from TMDB).
    /// </summary>
    public string Name => Title;

    /// <summary>
    /// Computed property that extracts the year from the release date.
    /// </summary>
    public string Year => IJellyseerrMedia.ExtractYear(ReleaseDate);

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
