using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using System.Text.Json;
using System.IO;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for syncing Jellyseerr data with Jellyfin libraries.
/// </summary>
public class JellyseerrSyncService
{
    private readonly ILogger<JellyseerrSyncService> _logger;
    private readonly JellyseerrApiService _apiService;
    private readonly ILibraryManager _libraryManager;
    private readonly JellyseerrBridgeService _bridgeService;

    public JellyseerrSyncService(
        ILogger<JellyseerrSyncService> logger,
        JellyseerrApiService apiService,
        ILibraryManager libraryManager,
        JellyseerrBridgeService bridgeService)
    {
        _logger = logger;
        _apiService = apiService;
        _libraryManager = libraryManager;
        _bridgeService = bridgeService;
    }

    /// <summary>
    /// Create folder structure and JSON metadata files for manual sync.
    /// </summary>
    public async Task<SyncResult> CreateBridgeFoldersAsync()
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
            var allShows = await _apiService.GetAllShowsAsync();

            _logger.LogInformation("Retrieved {MovieCount} movies, {ShowCount} TV shows from Jellyseerr",
                allMovies.Count, allShows.Count);

            // Create base directory
            var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
                _logger.LogInformation("Created base directory: {BaseDirectory}", baseDirectory);
            }

            // Process movies
            var movieResults = await CreateFoldersAsync(allMovies, "{Title} ({ReleaseDate}) [tmdbid-{Id}]");
            result.MoviesProcessed = movieResults.Processed;
            result.MoviesCreated = movieResults.Created;

            // Process TV shows
            var showResults = await CreateFoldersAsync(allShows, "{Name} ({FirstAirDate}) [tvdbid-{MediaInfo.TvdbId}] [tmdbid-{Id}]");
            result.ShowsProcessed = showResults.Processed;
            result.ShowsCreated = showResults.Created;

            result.Success = true;
            result.Message = $"Folder structure creation completed successfully. Created {result.MoviesCreated} movie folders, {result.ShowsCreated} show folders";
            result.Details = $"Movies: {result.MoviesCreated} folders created\n" +
                           $"Shows: {result.ShowsCreated} folders created\n" +
                           $"Base Directory: {baseDirectory}";

            _logger.LogInformation("Folder structure creation completed successfully");
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Directory not found during folder structure creation");
            result.Success = false;
            result.Message = $"Directory not found: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied during folder structure creation");
            result.Success = false;
            result.Message = $"Access denied: {ex.Message}";
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error during folder structure creation");
            result.Success = false;
            result.Message = $"I/O error: {ex.Message}";
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
            var allShows = new List<JellyseerrShow>();
            
            var networkDict = pluginConfig.GetNetworkMapDictionary();
            _logger.LogInformation("Fetching movies and TV shows for {NetworkCount} active networks: {Networks}", 
                networkDict.Count, string.Join(", ", networkDict.Values));
            
            // Get movies for all active networks
            allMovies = await _apiService.GetAllMoviesAsync();
            
            // Get TV shows for all active networks
            allShows = await _apiService.GetAllShowsAsync();
            
            var requestsResponse = await _apiService.GetRequestsAsync();
            var requests = requestsResponse?.Results ?? new List<JellyseerrRequest>();

            _logger.LogInformation("Retrieved {MovieCount} movies, {ShowCount} TV shows, {RequestCount} requests from Jellyseerr",
                allMovies.Count, allShows.Count, requests.Count);

            // Process requests
            var requestResults = await ProcessRequestsAsync(requests);
            result.RequestsProcessed = requestResults.Processed;

            result.Success = true;
            result.Message = $"Sync completed successfully. Processed {result.RequestsProcessed} requests";
            result.Details = $"Requests: {result.RequestsProcessed} processed\n" +
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
    /// Create folders and JSON metadata files for movies or TV shows.
    /// </summary>
    private async Task<ProcessResult> CreateFoldersAsync<T>(List<T> items, string template) where T : JellyseerrItem
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
                
                // Create folder name using the template
                var folderName = CreateFolderNameFromFormat(item, template);
                if (string.IsNullOrEmpty(folderName))
                {
                    _logger.LogWarning("Skipping item with missing required data: {Item}", item);
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
                var itemId = GetItemId(item);
                _logger.LogError(ex, "Error creating folder for {Item} (ID: {ItemId})", item, itemId);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Create folder name by filling template fields with item data using reflection.
    /// Supports nested property access like {mediaInfo.tvdbId}.
    /// </summary>
    private string CreateFolderNameFromFormat<T>(T item, string template) where T : JellyseerrItem
    {
        var folderName = template;
        
        // Find all fields in the template
        var fieldPattern = @"\{([^}]+)\}";
        var matches = System.Text.RegularExpressions.Regex.Matches(template, fieldPattern);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var field = match.Groups[0].Value; // Full field like "{title}"
            var propertyPath = match.Groups[1].Value; // Property path like "title" or "mediaInfo.tvdbId"
            
            var value = GetPropertyValue(item, propertyPath);
            folderName = folderName.Replace(field, value);
        }
        
        // Remove empty field blocks like [tvdbid-], [tmdbid-], [imdbid-] and empty parentheses ()
        folderName = System.Text.RegularExpressions.Regex.Replace(folderName, @"\s*(\[[^]]*-\]|\(\s*\))\s*", "");
        
        return SanitizeFileName(folderName);
    }
    
    /// <summary>
    /// Get property value using reflection, supporting nested property access.
    /// </summary>
    private string GetPropertyValue<T>(T item, string propertyPath) where T : JellyseerrItem
    {
        if (item == null || string.IsNullOrEmpty(propertyPath))
            return "";
        
        var currentObject = (object)item;
        var pathParts = propertyPath.Split('.');
        
        foreach (var part in pathParts)
        {
            if (currentObject == null)
                return "";
            
            var type = currentObject.GetType();
            var property = type.GetProperty(part);
            
            if (property == null)
                return "";
            
            currentObject = property.GetValue(currentObject);
        }
        
        if (currentObject == null)
            return "";
        
        // Convert to string
        return currentObject.ToString() ?? "";
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
    /// Get item ID for logging purposes.
    /// </summary>
    private object GetItemId<T>(T item)
    {
        return item switch
        {
            JellyseerrMovie movie => movie.Id,
            JellyseerrShow show => show.Id ?? 0,
            _ => 0
        };
    }

    /// <summary>
    /// Create JSON metadata file for movies or TV shows.
    /// </summary>
    private async Task CreateMetadataFileAsync<T>(T item, string directoryPath) where T : JellyseerrItem
    {
        // Serialize the item directly as its specific type to preserve all properties
        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
        var filePath = Path.Combine(directoryPath, "metadata.json");
        
        await File.WriteAllTextAsync(filePath, json);
        _logger.LogDebug("Created metadata file for {ItemType}: {Item}", item.Type, item);
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
    public int ShowsProcessed { get; set; }
    public int ShowsCreated { get; set; }
    public int ShowsUpdated { get; set; }
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

