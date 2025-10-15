using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using System.Text.Json;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for syncing Jellyseerr data with Jellyfin libraries.
/// </summary>
public partial class JellyseerrSyncService
{
    private readonly ILogger<JellyseerrSyncService> _logger;
    private readonly JellyseerrApiService _apiService;
    private readonly ILibraryManager _libraryManager;
    private readonly JellyseerrBridgeService _bridgeService;
    
    // Semaphore to ensure only one sync operation runs at a time
    private static readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

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
        // Check if another sync operation is already running
        if (!await _syncSemaphore.WaitAsync(TimeSpan.Zero))
        {
            _logger.LogWarning("Another sync operation is already running. Skipping this request.");
            return new SyncResult
            {
                Success = false,
                Message = "Another sync operation is already running. Please wait for it to complete.",
                MoviesResult = new ProcessResult(),
                ShowsResult = new ProcessResult()
            };
        }

        try
        {
            _logger.LogInformation("Starting Jellyseerr sync operation...");
            return await CreateBridgeFoldersInternalAsync();
        }
        finally
        {
            _syncSemaphore.Release();
            _logger.LogInformation("Jellyseerr sync operation completed and lock released.");
        }
    }

    /// <summary>
    /// Check if a sync operation is currently running.
    /// </summary>
    public bool IsSyncRunning => _syncSemaphore.CurrentCount == 0;

    /// <summary>
    /// Internal method that performs the actual sync operation.
    /// </summary>
    private async Task<SyncResult> CreateBridgeFoldersInternalAsync()
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
            if (!await TestConnectionAsync(config))
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping folder structure creation");
                result.Success = false;
                result.Message = "Failed to connect to Jellyseerr API";
                return result;
            }

            // Get data from Jellyseerr - handle network iteration business logic here
            var allMovies = new List<JellyseerrMovie>();
            var allShows = new List<JellyseerrShow>();
            
            // Get network ID-to-name mapping
            var networkDict = config.GetNetworkMapDictionary();
            
            // Loop through each active network for movies
            foreach (var networkId in networkDict.Keys)
            {
                if (networkDict.TryGetValue(networkId, out var networkName))
                {
                    _logger.LogInformation("Fetching movies for network: {NetworkName} (ID: {NetworkId})", networkName, networkId);
                    
                    var networkMovies = await _apiService.CallEndpointAsync(JellyseerrEndpoint.DiscoverMovies, config);
                    
                    if (networkMovies == null)
                    {
                        _logger.LogError("API call returned null for movies endpoint for network: {NetworkName}", networkName);
                        throw new InvalidOperationException($"Failed to retrieve movies for network {networkName} - API returned null");
                    }
                    
                    var movies = (List<JellyseerrMovie>)networkMovies;
                    
                    if (movies.Count == 0)
                    {
                        _logger.LogWarning("No movies returned for network: {NetworkName}", networkName);
                    }
                    
                    allMovies.AddRange(movies);
                    _logger.LogInformation("Retrieved {MovieCount} movies for {NetworkName}", movies.Count, networkName);
                }
                else
                {
                    _logger.LogWarning("Network '{NetworkName}' not found in available networks", networkName);
                }
            }
            
            // Loop through each active network for TV shows
            foreach (var networkId in networkDict.Keys)
            {
                if (networkDict.TryGetValue(networkId, out var networkName))
                {
                    _logger.LogInformation("Fetching TV shows for network: {NetworkName} (ID: {NetworkId})", networkName, networkId);
                    
                    var networkShows = await _apiService.CallEndpointAsync(JellyseerrEndpoint.DiscoverTv, config);
                    
                    if (networkShows == null)
                    {
                        _logger.LogError("API call returned null for TV shows endpoint for network: {NetworkName}", networkName);
                        throw new InvalidOperationException($"Failed to retrieve TV shows for network {networkName} - API returned null");
                    }
                    
                    var shows = (List<JellyseerrShow>)networkShows;
                    
                    if (shows.Count == 0)
                    {
                        _logger.LogWarning("No TV shows returned for network: {NetworkName}", networkName);
                    }
                    
                    allShows.AddRange(shows);
                    _logger.LogInformation("Retrieved {ShowCount} shows for {NetworkName}", shows.Count, networkName);
                }
                else
                {
                    _logger.LogWarning("Network '{NetworkName}' not found in available networks", networkName);
                }
            }

            _logger.LogInformation("Retrieved {MovieCount} movies, {ShowCount} TV shows from Jellyseerr",
                allMovies.Count, allShows.Count);

            // Check if we actually got any data from the API calls
            if (allMovies.Count == 0 && allShows.Count == 0)
            {
                _logger.LogError("No data retrieved from Jellyseerr API - all API calls returned empty results");
                result.Success = false;
                result.Message = "No data retrieved from Jellyseerr API. Check API connection and configuration.";
                result.Details = "All API calls returned empty results. This may indicate:\n" +
                               "- API connection issues\n" +
                               "- Invalid API key\n" +
                               "- JSON parsing errors (check logs for JsonException warnings)\n" +
                               "- Empty discover results for configured networks";
                return result;
            }

            // Create base directory
            var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
                _logger.LogInformation("Created base directory: {BaseDirectory}", baseDirectory);
            }

            // Process movies
            var movieResults = await CreateFoldersAsync(allMovies, "{Name} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]");

            // Process TV shows
            var showResults = await CreateFoldersAsync(allShows, "{Name} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]");

            result.Success = true;
            result.MoviesResult = movieResults;
            result.ShowsResult = showResults;
            result.Message = "Folder structure creation completed successfully";
            result.Details = result.ToString();

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
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }


    /// <summary>
    /// Process requests from Jellyseerr.
    /// </summary>
    private async Task<ProcessResult> ProcessRequestsAsync(List<MediaRequest> requests)
    {
        var result = new ProcessResult();
        
        foreach (var request in requests)
        {
            try
            {
                result.Processed++;
                
                _logger.LogDebug("Processing request {RequestId} for {MediaType} (ID: {MediaId})",
                    request.Id, request.Type.ToString(), request.Media?.Id ?? 0);

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
    private Task UpdateRequestStatusAsync(MediaRequest request)
    {
        _logger.LogDebug("Updating request status for {MediaType} (ID: {MediaId}): {Status}", 
            request.Type.ToString(), request.Media?.Id ?? 0, request.Status);
        
        // Update request status in Jellyfin metadata
        // Implementation depends on Jellyfin's internal APIs
        return Task.CompletedTask;
    }





    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows.
    /// </summary>
    private async Task<ProcessResult> CreateFoldersAsync<TJellyseerr>(List<TJellyseerr> items, string template) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem, IEquatable<TJellyseerr>
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
                    result.ItemsAdded.Add(item);
                    _logger.LogDebug("Created folder: {FolderName}", folderName);
                }
                else
                {
                    result.Updated++;
                    result.ItemsUpdated.Add(item);
                    _logger.LogDebug("Folder already exists: {FolderName}", folderName);
                }

                // Create JSON metadata file
                await CreateMetadataFileAsync(item, targetDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating folder for {Item}", item);
            }
        }
        
        return result;
    }

    
    /// <summary>
    /// Create folder name by filling template fields with item data using reflection.
    /// Supports nested property access like {mediaInfo.tvdbId}.
    /// </summary>
    private string CreateFolderNameFromFormat<TJellyseerr>(TJellyseerr item, string template) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem, IEquatable<TJellyseerr>
    {
        var folderName = template;
        
        // Find all fields in the template
        var fieldPattern = @"\{([^}]+)\}";
        var matches = System.Text.RegularExpressions.Regex.Matches(template, fieldPattern);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var field = match.Groups[0].Value; // Full field like "{title}"
            var propertyPath = match.Groups[1].Value; // Property path like "title" or "mediaInfo.tvdbId"
            
            var value = GetPropertyValue((IJellyseerrItem)item, propertyPath);
            folderName = folderName.Replace(field, value);
        }
        
        // Remove empty field blocks like [tvdbid-], [tmdbid-], [imdbid-] and empty parentheses ()
        folderName = System.Text.RegularExpressions.Regex.Replace(folderName, @"\s*(\[[^]]*-\]|\(\s*\))\s*", "");
        
        return SanitizeFileName(folderName);
    }

    /// <summary>
    /// Get property value using reflection.
    /// </summary>
    private string GetPropertyValue(IJellyseerrItem item, string propertyPath)
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
    /// Create JSON metadata file for movies or TV shows.
    /// </summary>
    private async Task CreateMetadataFileAsync<TJellyseerr>(TJellyseerr item, string directoryPath) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem, IEquatable<TJellyseerr>
    {
        // Serialize the item directly as its specific type to preserve all properties
        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
        var filePath = Path.Combine(directoryPath, "metadata.json");
        
        await File.WriteAllTextAsync(filePath, json);
        _logger.LogDebug("Created metadata file for {ItemType}: {Item}", typeof(TJellyseerr).Name, item);
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
    public ProcessResult MoviesResult { get; set; } = new();
    public ProcessResult ShowsResult { get; set; } = new();

    public override string ToString()
    {
        var details = new List<string>();
        
        if (MoviesResult.Processed > 0)
        {
            details.Add($"Movies ({MoviesResult.Processed} processed):");
            details.Add($"  {MoviesResult.ToString()}");
        }
        
        if (ShowsResult.Processed > 0)
        {
            details.Add($"Shows ({ShowsResult.Processed} processed):");
            details.Add($"  {ShowsResult.ToString()}");
        }
        
        return string.Join("\n", details);
    }
}

/// <summary>
/// Result of a processing operation.
/// </summary>
public class ProcessResult
{
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();

    public override string ToString()
    {
        var details = new List<string>();
        
        if (ItemsAdded.Count > 0)
        {
            var addedNames = ItemsAdded.Select(item => $"{item.Name} ({item.Year}) [{item.MediaType}]").ToList();
            details.Add($"Added: {string.Join(", ", addedNames)}");
        }
        
        if (ItemsUpdated.Count > 0)
        {
            var updatedNames = ItemsUpdated.Select(item => $"{item.Name} ({item.Year}) [{item.MediaType}]").ToList();
            details.Add($"Updated: {string.Join(", ", updatedNames)}");
        }
        
        return string.Join("\n", details);
    }
}


/// <summary>
/// Helper methods for business logic that was moved from JellyseerrApiService.
/// </summary>
public partial class JellyseerrSyncService
{
    /// <summary>
    /// Test connection to Jellyseerr API using the factory method.
    /// </summary>
    private async Task<bool> TestConnectionAsync(PluginConfiguration config)
    {
        try
        {
            // Use the factory method to get status - this automatically handles the correct response type
            var status = await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status, config);
            var statusResponse = (SystemStatus)status;
            
            // If we get a status response, the connection is working
            if (status != null)
            {
                _logger.LogInformation("Successfully connected to Jellyseerr API");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Jellyseerr API");
            return false;
        }
    }
}

