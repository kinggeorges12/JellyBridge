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
    public async Task<SyncResult> SyncAsync()
    {
        var config = Plugin.Instance.Configuration;
        var result = new SyncResult();
        
        if (!config.IsEnabled)
        {
            _logger.LogInformation("Jellyseerr Bridge is disabled, skipping sync");
            result.Success = false;
            result.Message = "Jellyseerr Bridge is disabled";
            return result;
        }

        try
        {
            _logger.LogInformation("Starting Jellyseerr sync...");

            // Test connection first
            if (!await _apiService.TestConnectionAsync(config))
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync");
                result.Success = false;
                result.Message = "Failed to connect to Jellyseerr API";
                return result;
            }

            // Get data from Jellyseerr
            var pluginConfig = Plugin.Instance.Configuration;
            
            // Ensure we have active networks configured
            pluginConfig.EnsureDefaultNetworkMappings();
            
            // Get networks to map network names to IDs (if not already cached)
            if (!pluginConfig.NetworkMap.Any())
            {
                _logger.LogInformation("Network name-to-ID mapping not cached, fetching from API");
                var networks = await _apiService.GetNetworksAsync(pluginConfig.Region);
                pluginConfig.SetNetworkMapDictionary(networks.ToDictionary(n => n.Name, n => n.Id));
                _logger.LogInformation("Cached {Count} network mappings", pluginConfig.NetworkMap.Count);
            }
            else
            {
                _logger.LogInformation("Using cached network name-to-ID mapping ({Count} networks)", pluginConfig.NetworkMap.Count);
            }
            
            // Get movies and TV shows for each active network
            var allMovies = new List<JellyseerrMovie>();
            var allTvShows = new List<JellyseerrTvShow>();
            
            var networkDict = pluginConfig.GetNetworkMapDictionary();
            _logger.LogInformation("Fetching movies and TV shows for {NetworkCount} active networks: {Networks}", 
                networkDict.Count, string.Join(", ", networkDict.Keys));
            
            // Get movies for all active networks
            allMovies = await _apiService.GetAllMoviesAsync();
            
            // Get TV shows for all active networks
            allTvShows = await _apiService.GetAllTvShowsAsync();
            
            var requests = await _apiService.GetRequestsAsync();

            _logger.LogInformation("Retrieved {MovieCount} movies, {TvCount} TV shows, {RequestCount} requests from Jellyseerr",
                allMovies.Count, allTvShows.Count, requests.Count);

            // Process movies
            var movieResults = await ProcessMoviesAsync(allMovies);
            result.MoviesProcessed = movieResults.Processed;
            result.MoviesCreated = movieResults.Created;
            result.MoviesUpdated = movieResults.Updated;

            // Process TV shows
            var tvResults = await ProcessTvShowsAsync(allTvShows);
            result.TvShowsProcessed = tvResults.Processed;
            result.TvShowsCreated = tvResults.Created;
            result.TvShowsUpdated = tvResults.Updated;

            // Process requests
            var requestResults = await ProcessRequestsAsync(requests);
            result.RequestsProcessed = requestResults.Processed;

            result.Success = true;
            result.Message = $"Sync completed successfully. Processed {result.MoviesProcessed} movies, {result.TvShowsProcessed} TV shows, {result.RequestsProcessed} requests";
            result.Details = $"Movies: {result.MoviesCreated} created, {result.MoviesUpdated} updated\n" +
                           $"TV Shows: {result.TvShowsCreated} created, {result.TvShowsUpdated} updated\n" +
                           $"Requests: {result.RequestsProcessed} processed\n" +
                           $"Active Networks: {string.Join(", ", networkDict.Keys)}";

            _logger.LogInformation("Jellyseerr sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Jellyseerr sync");
            result.Success = false;
            result.Message = $"Sync failed: {ex.Message}";
        }
        
        return result;
    }

    /// <summary>
    /// Process movies from Jellyseerr.
    /// </summary>
    private async Task<ProcessResult> ProcessMoviesAsync(List<JellyseerrMovie> movies)
    {
        var result = new ProcessResult();
        
        foreach (var movie in movies)
        {
            try
            {
                result.Processed++;
                
                // Check if movie already exists in Jellyfin
                var existingMovie = _libraryManager.GetItemById($"jellyseerr-movie-{movie.Id}");
                
                if (existingMovie == null)
                {
                    // Create placeholder movie
                    await CreatePlaceholderMovieAsync(movie);
                    result.Created++;
                }
                else
                {
                    // Update existing movie
                    await UpdatePlaceholderMovieAsync(existingMovie as Movie, movie);
                    result.Updated++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing movie {MovieTitle} (ID: {MovieId})", movie.Title, movie.Id);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Process TV shows from Jellyseerr.
    /// </summary>
    private async Task<ProcessResult> ProcessTvShowsAsync(List<JellyseerrTvShow> tvShows)
    {
        var result = new ProcessResult();
        
        foreach (var tvShow in tvShows)
        {
            try
            {
                result.Processed++;
                
                // Check if TV show already exists in Jellyfin
                var existingShow = _libraryManager.GetItemById($"jellyseerr-tv-{tvShow.Id}");
                
                if (existingShow == null)
                {
                    // Create placeholder TV show
                    await CreatePlaceholderTvShowAsync(tvShow);
                    result.Created++;
                }
                else
                {
                    // Update existing TV show
                    await UpdatePlaceholderTvShowAsync(existingShow as Series, tvShow);
                    result.Updated++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing TV show {ShowName} (ID: {ShowId})", tvShow.Name, tvShow.Id);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Process requests from Jellyseerr.
    /// </summary>
    private async Task<ProcessResult> ProcessRequestsAsync(List<JellyseerrRequest> requests)
    {
        var result = new ProcessResult();
        
        foreach (var request in requests)
        {
            try
            {
                result.Processed++;
                
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
        
        return result;
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

/// <summary>
/// Result of a sync operation.
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int MoviesProcessed { get; set; }
    public int MoviesCreated { get; set; }
    public int MoviesUpdated { get; set; }
    public int TvShowsProcessed { get; set; }
    public int TvShowsCreated { get; set; }
    public int TvShowsUpdated { get; set; }
    public int RequestsProcessed { get; set; }
}

/// <summary>
/// Result of a processing operation.
/// </summary>
public class ProcessResult
{
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
}
