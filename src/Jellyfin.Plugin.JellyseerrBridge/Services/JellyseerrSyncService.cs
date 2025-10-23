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
    /// Check if a sync operation is currently running.
    /// </summary>
    public bool IsSyncRunning => Plugin.IsOperationRunning;

    /// <summary>
    /// Sync folder structure and JSON metadata files for manual sync.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncResult> SyncBridgeFoldersAsync()
    {
        var result = new SyncResult();
        var isEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
        
        if (!isEnabled)
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
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping folder structure creation");
                result.Success = false;
                result.Message = "Failed to connect to Jellyseerr API";
                return result;
            }

            // Fetch movies for all networks
            var discoverMovies = await _apiService.FetchDiscoverMediaAsync<JellyseerrMovie>();
            
            // Fetch TV shows for all networks
            var discoverShows = await _apiService.FetchDiscoverMediaAsync<JellyseerrShow>();

            _logger.LogInformation("Retrieved {MovieCount} movies, {ShowCount} TV shows from Jellyseerr",
                discoverMovies.Count, discoverShows.Count);

            // Check if we actually got any data from the API calls
            if (discoverMovies.Count == 0 && discoverShows.Count == 0)
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
                discoverMovies.Count, discoverShows.Count);

            // Process movies
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 🎬 Starting movie folder creation...");
            var movieTask = _bridgeService.CreateFoldersAsync(discoverMovies);

            // Process TV shows
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 📺 Starting TV show folder creation...");
            var showTask = _bridgeService.CreateFoldersAsync(discoverShows);

            // Wait for both to complete
            await Task.WhenAll(movieTask, showTask);
            
            // Get the results
            var moviesResult = await movieTask;
            var showsResult = await showTask;

            // Run library scan to find matches and get unmatched items
            var (matchedItems, unmatchedItems) = await _bridgeService.LibraryScanAsync(discoverMovies, discoverShows);

            // Separate unmatched items by type for processing
            var unmatchedMovies = unmatchedItems.OfType<JellyseerrMovie>().ToList();
            var unmatchedShows = unmatchedItems.OfType<JellyseerrShow>().ToList();

            // Create placeholder videos only for unmatched items
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 🎬 Creating placeholder videos for {UnmatchedMovieCount} unmatched movies...", unmatchedMovies.Count);
            var processedMovies = await _bridgeService.CreatePlaceholderVideosAsync(unmatchedMovies);

            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 📺 Creating placeholder videos for {UnmatchedShowCount} unmatched shows...", unmatchedShows.Count);
            var processedShows = await _bridgeService.CreatePlaceholderVideosAsync(unmatchedShows);

            // Create season folders for unmatched TV shows only
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 📺 Creating season folders for {UnmatchedShowCount} unmatched shows...", unmatchedShows.Count);
            await _bridgeService.CreateSeasonFoldersForShows(unmatchedShows);

            // Clean up old metadata before refreshing library
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: 🧹 Cleaning up old metadata...");
            var cleanupResult = await _bridgeService.CleanupMetadataAsync();
            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: ✅ Cleanup completed - {CleanupResult}", cleanupResult.ToString());
            
            result.Success = true;
            result.MoviesResult = moviesResult;
            result.ShowsResult = showsResult;
            result.Message = "Folder structure creation completed successfully";
            result.Details = $"Movies: {moviesResult.ToString()}\nShows: {showsResult.ToString()}\nCleanup: {cleanupResult.ToString()}";

            _logger.LogInformation("[JellyseerrSyncService] CreateFolderStructureAsync: ✅ Folder structure creation completed successfully - Movies: {MovieCreated} created, {MovieUpdated} updated | Shows: {ShowCreated} created, {ShowUpdated} updated", 
                moviesResult.Created, moviesResult.Updated, showsResult.Created, showsResult.Updated);

            // Check if library management is enabled
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

