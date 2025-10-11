using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using System.Text.Json;
using System.IO;

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
    /// Create folder structure and JSON metadata files for manual sync.
    /// </summary>
    public async Task<SyncResult> CreateFolderStructureAsync()
    {
        var config = Plugin.GetConfiguration();
        var result = new SyncResult();
        
        if (!Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.IsEnabled)) ?? false)
        {
            _logger.LogInformation("Jellyseerr Bridge is disabled, skipping folder structure creation");
            result.Success = false;
            result.Message = "Jellyseerr Bridge is disabled";
            return result;
        }

        try
        {
            _logger.LogInformation("Starting folder structure creation...");

            // Test connection first
            if (!await _apiService.TestConnectionAsync(config))
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping folder structure creation");
                result.Success = false;
                result.Message = "Failed to connect to Jellyseerr API";
                return result;
            }

            // Get data from Jellyseerr
            var allMovies = await _apiService.GetAllMoviesAsync();
            var allTvShows = await _apiService.GetAllTvShowsAsync();

            _logger.LogInformation("Retrieved {MovieCount} movies, {TvCount} TV shows from Jellyseerr",
                allMovies.Count, allTvShows.Count);

            // Create base directory
            var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
                _logger.LogInformation("Created base directory: {BaseDirectory}", baseDirectory);
            }

            // Process movies
            var movieResults = await CreateFoldersAsync(allMovies, 
                "{title} ({year}) [imdbid-{imdbid}] [tmdbid-{tmdbid}]");
            result.MoviesProcessed = movieResults.Processed;
            result.MoviesCreated = movieResults.Created;

            // Process TV shows
            var tvResults = await CreateFoldersAsync(allTvShows, 
                "{title} ({year}) [tvdbid-{tvdbid}] [tmdbid-{tmdbid}]");
            result.TvShowsProcessed = tvResults.Processed;
            result.TvShowsCreated = tvResults.Created;

            result.Success = true;
            result.Message = $"Folder structure creation completed successfully. Created {result.MoviesCreated} movie folders, {result.TvShowsCreated} TV show folders";
            result.Details = $"Movies: {result.MoviesCreated} folders created\n" +
                           $"TV Shows: {result.TvShowsCreated} folders created\n" +
                           $"Base Directory: {baseDirectory}";

            _logger.LogInformation("Folder structure creation completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during folder structure creation");
            result.Success = false;
            result.Message = $"Folder structure creation failed: {ex.Message}";
        }
        
        return result;
    }

    /// <summary>
    /// Perform a full sync of Jellyseerr data.
    /// </summary>
    public async Task<SyncResult> SyncAsync()
    {
        var config = Plugin.GetConfiguration();
        var result = new SyncResult();
        
        if (!Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.IsEnabled)) ?? false)
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
            var pluginConfig = Plugin.GetConfiguration();
            
            // Get movies and TV shows for each active network
            var allMovies = new List<JellyseerrMovie>();
            var allTvShows = new List<JellyseerrTvShow>();
            
            var networkDict = pluginConfig.GetNetworkMapDictionary();
            _logger.LogInformation("Fetching movies and TV shows for {NetworkCount} active networks: {Networks}", 
                networkDict.Count, string.Join(", ", networkDict.Values));
            
            // Get movies for all active networks
            allMovies = await _apiService.GetAllMoviesAsync();
            
            // Get TV shows for all active networks
            allTvShows = await _apiService.GetAllTvShowsAsync();
            
            var requestsResponse = await _apiService.GetRequestsAsync();
            var requests = requestsResponse?.Results ?? new List<JellyseerrRequest>();

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
                           $"Active Networks: {string.Join(", ", networkDict.Values)}";

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
                var movieGuid = GenerateJellyseerrGuid("movie", movie.Id);
                var existingMovie = _libraryManager.GetItemById(movieGuid);
                
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
                if (tvShow.Id.HasValue)
                {
                    var tvShowGuid = GenerateJellyseerrGuid("tv", tvShow.Id.Value);
                    var existingShow = _libraryManager.GetItemById(tvShowGuid);
                    
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing TV show {ShowName} (ID: {ShowId})", tvShow.Name ?? "Unknown", tvShow.Id);
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
        _logger.LogDebug("Creating placeholder movie: {MovieTitle}", movie.Title);
        
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
        _logger.LogInformation("Creating placeholder TV show: {ShowName}", tvShow.Name ?? "Unknown");
        
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
        
        _logger.LogDebug("Updating placeholder TV show: {ShowName}", tvShow.Name ?? "Unknown");
        
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

    /// <summary>
    /// Generates a consistent GUID for Jellyseerr items based on type and ID.
    /// </summary>
    private Guid GenerateJellyseerrGuid(string type, int id)
    {
        // Create a deterministic GUID based on the type and ID
        var input = $"jellyseerr-{type}-{id}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        
        // Use first 16 bytes of hash to create GUID
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        
        return new Guid(guidBytes);
    }


    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows.
    /// </summary>
    private async Task<ProcessResult> CreateFoldersAsync<T>(List<T> items, string format)
    {
        var config = Plugin.GetConfiguration();
        var result = new ProcessResult();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var libraryPrefix = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryPrefix));
        var createSeparateLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.CreateSeparateLibraries)) ?? false;
        
        foreach (var item in items)
        {
            try
            {
                result.Processed++;
                
                // Create folder name using the provided format string
                var folderName = CreateFolderNameFromFormat(item, format);
                if (string.IsNullOrEmpty(folderName))
                {
                    var itemName = GetItemName(item);
                    _logger.LogWarning("Skipping item with missing required data: {ItemName}", itemName);
                    continue;
                }

                // Determine directory path
                var targetDirectory = createSeparateLibraries 
                    ? Path.Combine(baseDirectory, libraryPrefix, folderName)
                    : Path.Combine(baseDirectory, folderName);

                // Create directory if it doesn't exist
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    result.Created++;
                    _logger.LogDebug("Created folder: {FolderName}", folderName);
                }

                // Create JSON metadata file
                await CreateMetadataFileAsync(item, targetDirectory);
            }
            catch (Exception ex)
            {
                var itemName = GetItemName(item);
                var itemId = GetItemId(item);
                _logger.LogError(ex, "Error creating folder for {ItemName} (ID: {ItemId})", itemName, itemId);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Create folder name using format string with placeholders extracted from the item.
    /// </summary>
    private string CreateFolderNameFromFormat<T>(T item, string format)
    {
        var values = ExtractValuesFromItem(item);
        
        var folderName = format;
        
        // Replace placeholders with actual values
        foreach (var kvp in values)
        {
            var placeholder = $"{{{kvp.Key}}}";
            var value = kvp.Value ?? string.Empty;
            
            // Skip empty values for optional fields
            if (string.IsNullOrEmpty(value) && kvp.Key != "title" && kvp.Key != "year")
            {
                // Remove the entire placeholder block for optional fields
                folderName = System.Text.RegularExpressions.Regex.Replace(folderName, 
                    $@"\s*\[{kvp.Key}-{{{kvp.Key}}}\]", "");
            }
            else
            {
                folderName = folderName.Replace(placeholder, value);
            }
        }
        
        // Clean up any remaining empty brackets or extra spaces
        folderName = System.Text.RegularExpressions.Regex.Replace(folderName, @"\s*\[\s*\]", "");
        folderName = System.Text.RegularExpressions.Regex.Replace(folderName, @"\s+", " ").Trim();
        
        return SanitizeFileName(folderName);
    }

    /// <summary>
    /// Extract values from movie or TV show item for format string replacement.
    /// </summary>
    private Dictionary<string, string?> ExtractValuesFromItem<T>(T item)
    {
        return item switch
        {
            JellyseerrMovie movie => new Dictionary<string, string?>
            {
                ["title"] = movie.Title,
                ["year"] = ExtractYear(movie.ReleaseDate),
                ["imdbid"] = movie.MediaInfo?.ImdbId,
                ["tmdbid"] = movie.MediaInfo?.TmdbId?.ToString(),
                ["tvdbid"] = null
            },
            JellyseerrTvShow tvShow => new Dictionary<string, string?>
            {
                ["title"] = tvShow.Name,
                ["year"] = ExtractYear(tvShow.FirstAirDate),
                ["imdbid"] = null,
                ["tmdbid"] = tvShow.MediaInfo?.TmdbId?.ToString(),
                ["tvdbid"] = tvShow.MediaInfo?.TvdbId?.ToString()
            },
            _ => new Dictionary<string, string?>()
        };
    }

    /// <summary>
    /// Extract year from date string.
    /// </summary>
    private string ExtractYear(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return string.Empty;

        if (DateTime.TryParse(dateString, out var date))
        {
            return date.Year.ToString();
        }

        // Try to extract year from YYYY-MM-DD format
        if (dateString.Length >= 4 && int.TryParse(dateString.Substring(0, 4), out var year))
        {
            return year.ToString();
        }

        return string.Empty;
    }

    /// <summary>
    /// Sanitize filename by removing invalid characters.
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    /// <summary>
    /// Get item name for logging purposes.
    /// </summary>
    private string GetItemName<T>(T item)
    {
        return item switch
        {
            JellyseerrMovie movie => movie.Title,
            JellyseerrTvShow tvShow => tvShow.Name ?? "Unknown",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Get item ID for logging purposes.
    /// </summary>
    private object GetItemId<T>(T item)
    {
        return item switch
        {
            JellyseerrMovie movie => movie.Id,
            JellyseerrTvShow tvShow => tvShow.Id ?? 0,
            _ => 0
        };
    }

    /// <summary>
    /// Create JSON metadata file for movies or TV shows.
    /// </summary>
    private async Task CreateMetadataFileAsync<T>(T item, string directoryPath)
    {
        object metadata = item switch
        {
            JellyseerrMovie movie => new
            {
                Type = "Movie",
                Title = movie.Title,
                OriginalTitle = movie.OriginalTitle,
                Overview = movie.Overview,
                ReleaseDate = movie.ReleaseDate,
                Year = ExtractYear(movie.ReleaseDate),
                GenreIds = movie.GenreIds,
                OriginalLanguage = movie.OriginalLanguage,
                Popularity = movie.Popularity,
                VoteAverage = movie.VoteAverage,
                VoteCount = movie.VoteCount,
                BackdropPath = movie.BackdropPath,
                PosterPath = movie.PosterPath,
                Adult = movie.Adult,
                Video = movie.Video,
                MediaInfo = movie.MediaInfo,
                JellyseerrId = movie.Id,
                CreatedAt = DateTime.UtcNow
            },
            JellyseerrTvShow tvShow => new
            {
                Type = "TV Show",
                Name = tvShow.Name,
                OriginalName = tvShow.OriginalName,
                Overview = tvShow.Overview,
                FirstAirDate = tvShow.FirstAirDate,
                Year = ExtractYear(tvShow.FirstAirDate),
                GenreIds = tvShow.GenreIds,
                OriginCountry = tvShow.OriginCountry,
                OriginalLanguage = tvShow.OriginalLanguage,
                Popularity = tvShow.Popularity,
                VoteAverage = tvShow.VoteAverage,
                VoteCount = tvShow.VoteCount,
                BackdropPath = tvShow.BackdropPath,
                PosterPath = tvShow.PosterPath,
                MediaInfo = tvShow.MediaInfo,
                JellyseerrId = tvShow.Id,
                CreatedAt = DateTime.UtcNow
            },
            _ => throw new ArgumentException($"Unsupported item type: {typeof(T)}")
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var filePath = Path.Combine(directoryPath, "metadata.json");
        
        await File.WriteAllTextAsync(filePath, json);
        var itemName = GetItemName(item);
        _logger.LogDebug("Created metadata file for {ItemType}: {ItemName}", typeof(T).Name, itemName);
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

