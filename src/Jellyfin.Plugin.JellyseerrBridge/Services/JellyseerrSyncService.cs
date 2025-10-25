using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.Utils;
using Jellyfin.Plugin.JellyseerrBridge;
using JellyfinUser = Jellyfin.Data.Entities.User;
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
    private readonly JellyseerrLogger<JellyseerrSyncService> _logger;
    private readonly JellyseerrApiService _apiService;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly JellyseerrBridgeService _bridgeService;
    private readonly JellyseerrLibraryService _libraryService;
    

    public JellyseerrSyncService(
        ILogger<JellyseerrSyncService> logger,
        JellyseerrApiService apiService,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        JellyseerrBridgeService bridgeService,
        JellyseerrLibraryService libraryService)
    {
        _logger = new JellyseerrLogger<JellyseerrSyncService>(logger);
        _apiService = apiService;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _bridgeService = bridgeService;
        _libraryService = libraryService;
    }

    /// <summary>
    /// Check if a sync operation is currently running.
    /// </summary>
    public bool IsSyncRunning => Plugin.IsOperationRunning;

    /// <summary>
    /// Sync folder structure and JSON metadata files from Jellyseerr to Jellyfin.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncResult> SyncFromJellyseerr()
    {
        var result = new SyncResult();

        try
        {
            _logger.LogDebug("Starting sync from Jellyseerr to Jellyfin...");

            // Test connection first
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync from Jellyseerr to Jellyfin");
                result.Success = false;
                result.Message = "Failed to connect to Jellyseerr API";
                return result;
            }

            // Fetch movies for all networks
            var discoverMovies = await _apiService.FetchDiscoverMediaAsync<JellyseerrMovie>();
            
            // Fetch TV shows for all networks
            var discoverShows = await _apiService.FetchDiscoverMediaAsync<JellyseerrShow>();

            _logger.LogDebug("Retrieved {MovieCount} movies, {ShowCount} TV shows from Jellyseerr",
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

            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: Processing {MovieCount} movies and {ShowCount} shows from Jellyseerr", 
                discoverMovies.Count, discoverShows.Count);

            // Process movies
            _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: 🎬 Creating Jellyfin folders for movies from Jellyseerr...");
            var movieTask = _bridgeService.CreateFoldersAsync(discoverMovies);

            // Process TV shows
            _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: 📺 Creating Jellyfin folders for TV shows from Jellyseerr...");
            var showTask = _bridgeService.CreateFoldersAsync(discoverShows);

            // Wait for both to complete
            await Task.WhenAll(movieTask, showTask);
            
            // Get the results
            var moviesResult = await movieTask;
            var showsResult = await showTask;

            // Run library scan to find matches and get unmatched items
            var (matchedItems, unmatchedItems) = await _bridgeService.LibraryScanAsync(discoverMovies, discoverShows);

            // Create ignore files for matched items
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🔄 Creating ignore files for {MatchCount} items already in Jellyfin library", matchedItems.Count);
            await _bridgeService.CreateIgnoreFilesAsync(matchedItems);

            // Separate unmatched items by type for processing
            var unmatchedMovies = unmatchedItems.OfType<JellyseerrMovie>().ToList();
            var unmatchedShows = unmatchedItems.OfType<JellyseerrShow>().ToList();

            // Create placeholder videos only for unmatched items
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🎬 Creating placeholder videos for {UnmatchedMovieCount} movies not in Jellyfin library...", unmatchedMovies.Count);
            var processedMovies = await _bridgeService.CreatePlaceholderVideosAsync(unmatchedMovies);

            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 📺 Creating placeholder videos for {UnmatchedShowCount} TV shows not in Jellyfin library...", unmatchedShows.Count);
            var processedShows = await _bridgeService.CreatePlaceholderVideosAsync(unmatchedShows);

            // Create season folders for unmatched TV shows only
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 📺 Creating season folders for {UnmatchedShowCount} TV shows not in Jellyfin library...", unmatchedShows.Count);
            //TODO: Uncomment this if nfo files don't work
            //await _bridgeService.CreateSeasonFoldersForShows(unmatchedShows);

            // Clean up old metadata before refreshing library
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🧹 Cleaning up old metadata from Jellyseerr bridge folder...");
            var cleanupResult = await _bridgeService.CleanupMetadataAsync();
            
            result.Success = true;
            result.MoviesResult = moviesResult;
            result.ShowsResult = showsResult;
            result.Message = "Sync from Jellyseerr to Jellyfin completed successfully";
            result.Details = $"Movies: {moviesResult.ToString()}\nShows: {showsResult.ToString()}\nCleanup: {cleanupResult.ToString()}";

            _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: ✅ Sync from Jellyseerr to Jellyfin completed successfully - Movies: {MovieCreated} created, {MovieUpdated} updated | Shows: {ShowCreated} created, {ShowUpdated} updated", 
                moviesResult.Created, moviesResult.Updated, showsResult.Created, showsResult.Updated);

            // Check if library management is enabled
            _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: 🔄 Refreshing Jellyfin library with synced content...");
            var refreshSuccess = _libraryService.RefreshJellyseerrLibrary();
            if (refreshSuccess)
            {
                _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: ✅ Jellyfin library refresh started successfully");
                result.Message += " and library managed";
            }
            else
            {
                _logger.LogWarning("[JellyseerrSyncService] SyncFromJellyseerr: ⚠️ Jellyfin library refresh failed");
                result.Message += " (library management failed)";
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Directory not found during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"Directory not found: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"Access denied: {ex.Message}";
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"I/O error: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"Sync from Jellyseerr to Jellyfin failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }

    /// <summary>
    /// Sync data from Jellyfin to Jellyseerr by creating requests for favorited bridge-only items.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncResult> SyncToJellyseerr()
    {
        var result = new SyncResult();

        try
        {
            _logger.LogDebug("Starting sync to Jellyseerr...");
            
            // Step 1: Get bridge folder items directly from Jellyfin
            var bridgeLibraryPath = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            
            // Get bridge-only items directly from Jellyfin
            var bridgeMovies = JellyfinHelper.GetExistingItems<Movie>(_libraryManager, bridgeLibraryPath);
            var bridgeShows = JellyfinHelper.GetExistingItems<Series>(_libraryManager, bridgeLibraryPath);
            
            var bridgeOnlyItems = new List<BaseItem>();
            bridgeOnlyItems.AddRange(bridgeMovies);
            bridgeOnlyItems.AddRange(bridgeShows);
            
            if (bridgeOnlyItems.Count == 0)
            {
                _logger.LogWarning("No bridge-only items found in Jellyseerr bridge folder: {BridgePath}", bridgeLibraryPath);
                result.Success = true;
                result.Message = "No bridge-only items found to sync";
                return result;
            }
            
            _logger.LogDebug("Found {BridgeOnlyCount} bridge-only items in Jellyseerr bridge folder: {BridgePath}", bridgeOnlyItems.Count, bridgeLibraryPath);
            
            // Step 2: Get all Jellyfin users and their favorites
            var allFavorites = JellyfinHelper.GetUserFavorites(_userManager, _libraryManager, _userDataManager);
            _logger.LogDebug("Retrieved favorites for {UserCount} users", allFavorites.Count);
            
            var totalFavorites = allFavorites.Values.Sum(favs => favs.Count);
            _logger.LogDebug("Total favorites across all users: {TotalFavorites}", totalFavorites);
            
            foreach (var (user, favorites) in allFavorites)
            {
                if (favorites.Count > 0)
                {
                    _logger.LogTrace("User '{Username}' has {FavoriteCount} favorites", user.Username, favorites.Count);
                }
            }
            
            // Step 3: Get all Jellyseerr users for request creation
            var jellyseerrUsers = await _bridgeService.GetJellyseerrUsersAsync();
            if (jellyseerrUsers == null || jellyseerrUsers.Count == 0)
            {
                _logger.LogWarning("No Jellyseerr users found");
                result.Success = false;
                result.Message = "No Jellyseerr users found";
                return result;
            }
            
            // Step 4: Group bridge-only items by TMDB ID and find first user who favorited each
            var uniqueItemsWithJellyseerrUser = _bridgeService.EnsureFirstJellyseerrUser(bridgeOnlyItems, allFavorites, jellyseerrUsers);
            _logger.LogDebug("Found {UniqueCount} unique bridge-only items with favorited Jellyseerr users (from {TotalCount} total)", 
                uniqueItemsWithJellyseerrUser.Count, bridgeOnlyItems.Count);
            
            // Step 5 & 6: Create requests for favorited bridge-only items
            var requestResults = await _bridgeService.RequestFavorites(uniqueItemsWithJellyseerrUser);
            
            result.Success = true;
            result.Message = $"Sync to Jellyseerr completed successfully - Created {requestResults.Count} requests";
            result.Details = $"Processed {bridgeOnlyItems.Count} bridge-only items, created {requestResults.Count} requests for favorited items";
            
            _logger.LogDebug("Sync to Jellyseerr completed with {ResultCount} successful requests", requestResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync to Jellyseerr");
            result.Success = false;
            result.Message = $"Sync to Jellyseerr failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }
}

