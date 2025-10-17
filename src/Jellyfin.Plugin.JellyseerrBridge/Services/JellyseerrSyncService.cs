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
    private readonly JellyseerrLibraryService _libraryService;
    
    // Semaphore to ensure only one sync operation runs at a time
    private static readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

    public JellyseerrSyncService(
        ILogger<JellyseerrSyncService> logger,
        JellyseerrApiService apiService,
        ILibraryManager libraryManager,
        JellyseerrBridgeService bridgeService,
        JellyseerrLibraryService libraryService)
    {
        _logger = logger;
        _apiService = apiService;
        _libraryManager = libraryManager;
        _bridgeService = bridgeService;
        _libraryService = libraryService;
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

            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: Processing {MovieCount} movies and {ShowCount} shows", 
                allMovies.Count, allShows.Count);

            // Process movies
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 🎬 Starting movie folder creation...");
            var movieTask = _bridgeService.CreateFoldersAsync(allMovies);

            // Process TV shows
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 📺 Starting TV show folder creation...");
            var showTask = _bridgeService.CreateFoldersAsync(allShows);

            // Wait for both to complete
            await Task.WhenAll(movieTask, showTask);

            // Run library scan to find matches and create ignore files
            var matchedItems = await _bridgeService.LibraryScanAsync(allMovies, allShows);

            // Separate matched items by type
            var matchedMovies = matchedItems.OfType<JellyseerrMovie>().ToList();
            var matchedShows = matchedItems.OfType<JellyseerrShow>().ToList();

            // Create placeholder videos only for non-ignored items
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 🎬 Creating placeholder videos for non-ignored movies...");
            var moviePlaceholderTask = _bridgeService.CreatePlaceholderVideosAsync(allMovies, matchedMovies);

            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 📺 Creating placeholder videos for non-ignored shows...");
            var showPlaceholderTask = _bridgeService.CreatePlaceholderVideosAsync(allShows, matchedShows);

            // Wait for placeholder creation to complete
            await Task.WhenAll(moviePlaceholderTask, showPlaceholderTask);

            // Get the results
            result.Success = true;
            result.MoviesResult = await movieTask;
            result.ShowsResult = await showTask;
            result.Message = "Folder structure creation completed successfully";
            result.Details = result.ToString();

            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: ✅ Folder structure creation completed successfully - Movies: {MovieCreated} created, {MovieUpdated} updated | Shows: {ShowCreated} created, {ShowUpdated} updated", 
                result.MoviesResult.Created, result.MoviesResult.Updated, result.ShowsResult.Created, result.ShowsResult.Updated);

            // Check if library management is enabled
            if (config?.ManageJellyseerrLibrary == true)
            {
                _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 🔄 Managing Jellyseerr library...");
                var refreshSuccess = await _libraryService.RefreshJellyseerrLibraryAsync();
                if (refreshSuccess)
                {
                    _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: ✅ Jellyseerr library management completed successfully");
                    result.Message += " and library managed";
                }
                else
                {
                    _logger.LogWarning("[JellyseerrSyncService] CreateFolderStructureAsync: ⚠️ Jellyseerr library management failed");
                    result.Message += " (library management failed)";
                }
            }
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
        return $"Movies: {MoviesResult.Created} created, {MoviesResult.Updated} updated | Shows: {ShowsResult.Created} created, {ShowsResult.Updated} updated";
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

