using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for syncing Jellyseerr data with Jellyfin libraries.
/// </summary>
public class JellyseerrSyncService
{
    private readonly ILogger<JellyseerrSyncService> _logger;
    private readonly JellyseerrApiService _apiService;
    private readonly ILibraryManager _libraryManager;

    public JellyseerrSyncService(
        ILogger<JellyseerrSyncService> logger,
        JellyseerrApiService apiService,
        ILibraryManager libraryManager)
    {
        _logger = logger;
        _apiService = apiService;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Perform a full sync of Jellyseerr data.
    /// </summary>
    public async Task SyncAsync()
    {
        var config = Plugin.Instance.Configuration;
        
        if (!config.IsEnabled)
        {
            _logger.LogInformation("Jellyseerr Bridge is disabled, skipping sync");
            return;
        }

        try
        {
            _logger.LogInformation("Starting Jellyseerr sync...");

            // Test connection first
            if (!await _apiService.TestConnectionAsync(config))
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync");
                return;
            }

            // Get data from Jellyseerr
            var movies = await _apiService.GetMoviesAsync(config);
            var tvShows = await _apiService.GetTvShowsAsync(config);
            var requests = await _apiService.GetRequestsAsync(config);

            _logger.LogInformation("Retrieved {MovieCount} movies, {TvCount} TV shows, {RequestCount} requests from Jellyseerr",
                movies.Count, tvShows.Count, requests.Count);

            // Process movies
            await ProcessMoviesAsync(movies);

            // Process TV shows
            await ProcessTvShowsAsync(tvShows);

            // Process requests
            await ProcessRequestsAsync(requests);

            _logger.LogInformation("Jellyseerr sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Jellyseerr sync");
        }
    }

    /// <summary>
    /// Process movies from Jellyseerr.
    /// </summary>
    private async Task ProcessMoviesAsync(List<JellyseerrMovie> movies)
    {
        foreach (var movie in movies)
        {
            try
            {
                // Check if movie already exists in Jellyfin
                var existingMovie = _libraryManager.GetItemById($"jellyseerr-movie-{movie.Id}");
                
                if (existingMovie == null)
                {
                    // Create placeholder movie
                    await CreatePlaceholderMovieAsync(movie);
                }
                else
                {
                    // Update existing movie
                    await UpdatePlaceholderMovieAsync(existingMovie as Movie, movie);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing movie {MovieTitle} (ID: {MovieId})", movie.Title, movie.Id);
            }
        }
    }

    /// <summary>
    /// Process TV shows from Jellyseerr.
    /// </summary>
    private async Task ProcessTvShowsAsync(List<JellyseerrTvShow> tvShows)
    {
        foreach (var tvShow in tvShows)
        {
            try
            {
                // Check if TV show already exists in Jellyfin
                var existingShow = _libraryManager.GetItemById($"jellyseerr-tv-{tvShow.Id}");
                
                if (existingShow == null)
                {
                    // Create placeholder TV show
                    await CreatePlaceholderTvShowAsync(tvShow);
                }
                else
                {
                    // Update existing TV show
                    await UpdatePlaceholderTvShowAsync(existingShow as Series, tvShow);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing TV show {ShowName} (ID: {ShowId})", tvShow.Name, tvShow.Id);
            }
        }
    }

    /// <summary>
    /// Process requests from Jellyseerr.
    /// </summary>
    private async Task ProcessRequestsAsync(List<JellyseerrRequest> requests)
    {
        foreach (var request in requests)
        {
            try
            {
                _logger.LogDebug("Processing request {RequestId} for {MediaType} (ID: {MediaId})",
                    request.Id, request.Media?.MediaType ?? "Unknown", request.Media?.Id ?? 0);

                // Update request status in Jellyfin metadata
                await UpdateRequestStatusAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request {RequestId}", request.Id);
            }
        }
    }

    /// <summary>
    /// Create a placeholder movie in Jellyfin.
    /// </summary>
    private Task CreatePlaceholderMovieAsync(JellyseerrMovie movie)
    {
        _logger.LogInformation("Creating placeholder movie: {MovieTitle}", movie.Title);
        
        // This would create a placeholder movie item in Jellyfin
        // Implementation depends on Jellyfin's internal APIs
        // For now, just log the action
        return Task.CompletedTask;
    }

    /// <summary>
    /// Update an existing placeholder movie.
    /// </summary>
    private Task UpdatePlaceholderMovieAsync(Movie? existingMovie, JellyseerrMovie movie)
    {
        if (existingMovie == null) return Task.CompletedTask;
        
        _logger.LogDebug("Updating placeholder movie: {MovieTitle}", movie.Title);
        
        // Update movie metadata
        // Implementation depends on Jellyfin's internal APIs
        return Task.CompletedTask;
    }

    /// <summary>
    /// Create a placeholder TV show in Jellyfin.
    /// </summary>
    private Task CreatePlaceholderTvShowAsync(JellyseerrTvShow tvShow)
    {
        _logger.LogInformation("Creating placeholder TV show: {ShowName}", tvShow.Name);
        
        // This would create a placeholder TV show item in Jellyfin
        // Implementation depends on Jellyfin's internal APIs
        // For now, just log the action
        return Task.CompletedTask;
    }

    /// <summary>
    /// Update an existing placeholder TV show.
    /// </summary>
    private Task UpdatePlaceholderTvShowAsync(Series? existingShow, JellyseerrTvShow tvShow)
    {
        if (existingShow == null) return Task.CompletedTask;
        
        _logger.LogDebug("Updating placeholder TV show: {ShowName}", tvShow.Name);
        
        // Update TV show metadata
        // Implementation depends on Jellyfin's internal APIs
        return Task.CompletedTask;
    }

    /// <summary>
    /// Update request status in Jellyfin metadata.
    /// </summary>
    private Task UpdateRequestStatusAsync(JellyseerrRequest request)
    {
        _logger.LogDebug("Updating request status for {MediaType} (ID: {MediaId}): {Status}", 
            request.Media?.MediaType ?? "Unknown", request.Media?.Id ?? 0, request.Status);
        
        // Update request status in Jellyfin metadata
        // Implementation depends on Jellyfin's internal APIs
        return Task.CompletedTask;
    }
}
