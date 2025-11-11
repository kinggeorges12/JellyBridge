using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's Movie class.
/// Provides additional functionality for Jellyseerr bridge operations.
/// </summary>
public class JellyfinMovie : WrapperBase<Movie>, IJellyfinItem
{
    /// <summary>
    /// The BaseItemKind type for movies.
    /// </summary>
    public BaseItemKind TypeName => BaseItemKind.Movie;

    public JellyfinMovie(Movie movie) : base(movie) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Create a JellyfinMovie from a MediaBrowser Movie.
    /// </summary>
    /// <param name="movie">The MediaBrowser Movie to wrap</param>
    /// <returns>A new JellyfinMovie instance</returns>
    public static JellyfinMovie FromMovie(Movie movie)
    {
        return new JellyfinMovie(movie);
    }

    /// <summary>
    /// Create a JellyfinMovie from a BaseItem. Throws ArgumentException if the item is not a Movie.
    /// </summary>
    /// <param name="item">The BaseItem to wrap (must be a Movie)</param>
    /// <returns>A new JellyfinMovie instance</returns>
    /// <exception cref="ArgumentException">Thrown if the item is not a Movie</exception>
    public static JellyfinMovie FromItem(BaseItem item)
    {
        if (item is Movie movie)
        {
            return new JellyfinMovie(movie);
        }
        throw new ArgumentException($"Item is not a Movie. Type: {item?.GetType().Name}", nameof(item));
    }

    /// <summary>
    /// Get the underlying Movie instance.
    /// </summary>
    /// <returns>The Movie instance or null if not available</returns>
    public Movie? GetMovie()
    {
        return Inner;
    }

    /// <summary>
    /// Get the ID of this movie.
    /// </summary>
    public Guid Id => Inner.Id;

    /// <summary>
    /// Get the name of this movie.
    /// </summary>
    public string Name => Inner.Name;

    /// <summary>
    /// Get the path of this movie.
    /// </summary>
    public string Path => Inner.Path;

    /// <summary>
    /// Get the provider IDs for this movie.
    /// </summary>
    public Dictionary<string, string> ProviderIds => Inner.ProviderIds;

    /// <summary>
    /// Extract TMDB ID from movie metadata.
    /// </summary>
    /// <returns>TMDB ID if found, null otherwise</returns>
    public int? GetTmdbId()
    {
        try
        {
            if (ProviderIds.TryGetValue("Tmdb", out var providerId) && !string.IsNullOrEmpty(providerId))
            {
                if (int.TryParse(providerId, out var id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // Ignore errors and return null
        }
        
        return null;
    }

    /// <summary>
    /// Get a provider ID by name.
    /// </summary>
    /// <param name="name">The provider name</param>
    /// <returns>The provider ID if found, null otherwise</returns>
    public string? GetProviderId(string name)
    {
        try
        {
            if (ProviderIds.TryGetValue(name, out var providerId))
            {
                return providerId;
            }
        }
        catch
        {
            // Ignore errors and return null
        }
        
        return null;
    }

    /// <summary>
    /// Check if two JellyfinMovie objects match by comparing IDs.
    /// </summary>
    /// <param name="other">Other JellyfinMovie to compare</param>
    /// <returns>True if the movies match, false otherwise</returns>
    public bool ItemsMatch(IJellyfinItem other)
    {
        if (other == null) return false;
        return Id == other.Id;
    }

    /// <summary>
    /// Serialize the item to JSON using the provided DTO service.
    /// </summary>
    /// <param name="dtoService">The DTO service to use for serialization</param>
    /// <returns>JSON representation of the item</returns>
    public string? ToJson(MediaBrowser.Controller.Dto.IDtoService dtoService)
    {
        try
        {
            var dtoOptions = new MediaBrowser.Controller.Dto.DtoOptions();
            var baseItemDto = dtoService.GetBaseItemDto(Inner, dtoOptions);
            return System.Text.Json.JsonSerializer.Serialize(baseItemDto);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to set the movie play status.
    /// </summary>
    /// <param name="user">The user for which to set the play status.</param>
    /// <param name="userDataManager">The user data manager to use for updating play status.</param>
    /// <param name="markAsPlayed">If true, marks as played; if false, marks as not played.</param>
    /// <returns>Result object containing success status and detailed information about the operation</returns>
    public JellyfinWrapperResult TrySetMoviePlayCount(JellyfinUser user, JellyfinIUserDataManager userDataManager, bool markAsPlayed)
    {
        // Check current play status and update if needed
        // Marking as played prevents the movie from appearing in unplayed counts (movie badge shows checkmark)
        // Marking as not played restores the unplayed badge
        var userData = userDataManager.GetUserData(user, Inner);
        if (userData == null)
        {
            return new JellyfinWrapperResult
            {
                Success = false,
                Message = $"UserData is null for movie '{Name}' (Id: {Id})"
            };
        }
        
        // Only update if the current status doesn't match the desired status
        if (userData.Played != markAsPlayed)
        {
            // Update play status - use the inner UserDataManager directly to set Played for this movie
            var userEntity = user.Inner;
            userData.Played = markAsPlayed;
            userDataManager.Inner.SaveUserData(userEntity, Inner, userData, MediaBrowser.Model.Entities.UserDataSaveReason.Import, System.Threading.CancellationToken.None);
            
            var status = markAsPlayed ? "played" : "not played";
            return new JellyfinWrapperResult
            {
                Success = true,
                Message = $"Marked movie '{Name}' (Id: {Id}) as {status}"
            };
        }
        
        // Already in the desired state, no need to update
        var currentStatus = userData.Played ? "played" : "not played";
        return new JellyfinWrapperResult
        {
            Success = false,
            Message = $"Movie '{Name}' (Id: {Id}) is already marked as {currentStatus}"
        };
    }
}
