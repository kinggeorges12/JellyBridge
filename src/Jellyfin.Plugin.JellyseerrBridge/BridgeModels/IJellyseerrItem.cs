using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Common interface for Jellyseerr bridge models that provides standardized metadata access.
/// 
/// This interface ensures consistent property access across all Jellyseerr bridge models,
/// enabling dynamic property mapping without hardcoded type checks.
/// 
/// Used by:
/// - JellyseerrMovie
/// - JellyseerrShow
/// - Any future bridge models that need standardized metadata access
/// </summary>
public interface IJellyseerrItem : IEquatable<BaseItem>
{
    /// <summary>
    /// Gets the type of this Jellyseerr object (e.g., "Movie", "Show").
    /// </summary>
    string Type { get; }
    
    /// <summary>
    /// Gets the TMDB ID of this item.
    /// </summary>
    int Id { get; }
    
    /// <summary>
    /// Gets the display name of this item (Title for movies, Name for shows).
    /// </summary>
    string? Name { get; }
    
    /// <summary>
    /// Gets the IMDB ID of this item.
    /// </summary>
    string? ImdbId { get; }
    
    /// <summary>
    /// Gets the TVDB ID of this item (null for movies).
    /// </summary>
    int? TvdbId { get; }
    
    /// <summary>
    /// Gets the release date of this item (ReleaseDate for movies, FirstAirDate for shows).
    /// </summary>
    string? ReleaseDate { get; }
}
