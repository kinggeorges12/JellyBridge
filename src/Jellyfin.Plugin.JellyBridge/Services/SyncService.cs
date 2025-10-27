using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge;
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

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for syncing Jellyseerr data with Jellyfin libraries.
/// </summary>
public partial class SyncService
{
    private readonly DebugLogger<SyncService> _logger;
    private readonly ApiService _apiService;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly BridgeService _bridgeService;
    private readonly LibraryService _libraryService;
    

    public SyncService(
        ILogger<SyncService> logger,
        ApiService apiService,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        BridgeService bridgeService,
        LibraryService libraryService)
    {
        _logger = new DebugLogger<SyncService>(logger);
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
    public async Task<SyncJellyseerrResult> SyncFromJellyseerr()
    {
        var result = new SyncJellyseerrResult();

        try
        {
            _logger.LogDebug("Starting sync from Jellyseerr to Jellyfin...");

            // Test connection first
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync from Jellyseerr to Jellyfin");
                result.Success = false;
                result.Message = "🔌 Failed to connect to Jellyseerr API";
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
                result.Message = "📭 No data retrieved from Jellyseerr API. Check API connection and configuration.";
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
            
            // Get the results and set them immediately
            var (addedMovies, updatedMovies) = await movieTask;
            var (addedShows, updatedShows) = await showTask;
            result.AddedMovies = addedMovies;
            result.UpdatedMovies = updatedMovies;
            result.AddedShows = addedShows;
            result.UpdatedShows = updatedShows;

            // Run library scan to find matches and get unmatched items
            var (matchedItems, unmatchedItems) = await _bridgeService.LibraryScanAsync(discoverMovies, discoverShows);

            // Create ignore files for matched items
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🔄 Creating ignore files for {MatchCount} items already in Jellyfin library",
                matchedItems.Count);
            var ignoreTask = _bridgeService.CreateIgnoreFilesAsync(matchedItems);

            var unmatchedShows = unmatchedItems.OfType<JellyseerrShow>().ToList();
            var unmatchedMovies = unmatchedItems.OfType<JellyseerrMovie>().ToList();
            // Create placeholder videos for unmatched movies only
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🎬 Creating placeholder videos for {UnmatchedMovieCount} unmatched movies not in Jellyfin library...", 
                unmatchedMovies.Count);
            var placeholderMovieTask = _bridgeService.CreatePlaceholderVideosAsync(unmatchedMovies);

            // Create season folders for unmatched TV shows only
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 📺 Creating season folders for {UnmatchedShowCount} TV shows not in Jellyfin library...", unmatchedShows.Count);
            var placeholderShowTask = _bridgeService.CreateSeasonFoldersForShows(unmatchedShows);
            
            await Task.WhenAll(ignoreTask, placeholderMovieTask, placeholderShowTask);

            // Clean up old metadata before refreshing library
            _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🧹 Cleaning up old metadata from Jellyseerr bridge folder...");
            var (deletedMovies, deletedShows) = await _bridgeService.CleanupMetadataAsync();
            result.DeletedMovies = deletedMovies;
            result.DeletedShows = deletedShows;
            
            result.Success = true;
            result.Message = "✅ Sync from Jellyseerr to Jellyfin completed successfully";
            result.Details = $"Movies: {addedMovies.Count} added, {updatedMovies.Count} updated, {deletedMovies.Count} deleted | Shows: {addedShows.Count} added, {updatedShows.Count} updated, {deletedShows.Count} deleted";

            _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: ✅ Sync from Jellyseerr to Jellyfin completed successfully - Movies: {MovieAdded} added, {MovieUpdated} updated | Shows: {ShowAdded} added, {ShowUpdated} updated", 
                addedMovies.Count, updatedMovies.Count, addedShows.Count, updatedShows.Count);

            // Check if library management is enabled
            var ranFirstTime = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RanFirstTime));
            var itemsDeleted = deletedMovies.Count > 0 || deletedShows.Count > 0;
            bool? refreshSuccess = false;
            
            if (ranFirstTime)
            {
                // Normal operation - only refresh Jellyseerr libraries
                _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🔄 Refreshing Jellyseerr library with synced content... (FullRefresh: {FullRefresh}, ItemsDeleted: {Deleted})", itemsDeleted, itemsDeleted);
                refreshSuccess = _libraryService.RefreshJellyseerrLibrary(fullRefresh: itemsDeleted);
            }
            else
            {
                // First time running - scan all libraries
                _logger.LogDebug("[JellyseerrSyncService] SyncFromJellyseerr: 🔄 First-time initialization - scanning all Jellyfin libraries for cleanup...");
                refreshSuccess = _libraryService.ScanAllLibrariesForFirstTime();
            }
            if (refreshSuccess == true)
            {
                _logger.LogTrace("[JellyseerrSyncService] SyncFromJellyseerr: ✅ Jellyfin library refresh started successfully");
                var refreshType = !ranFirstTime ? "first-time full scan" : (itemsDeleted ? "full" : "partial");
                result.Message += $" and started {refreshType} library refresh";
            }
            else if (refreshSuccess == false)
            {
                _logger.LogWarning("[JellyseerrSyncService] SyncFromJellyseerr: ⚠️ Jellyfin library refresh failed");
                result.Message += " (library management failed)";
            }
            // refresh success is null if library management is disabled
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Directory not found during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"📁 Directory not found: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"🚫 Access denied: {ex.Message}";
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"💾 I/O error: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"❌ Sync from Jellyseerr to Jellyfin failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }

    /// <summary>
    /// Sync data from Jellyfin to Jellyseerr by creating requests for favorited bridge-only items.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncJellyfinResult> SyncToJellyseerr()
    {
        var result = new SyncJellyfinResult();

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
                _logger.LogWarning("No Jellyseerr bridge items found in folder: {BridgePath}", bridgeLibraryPath);
                result.Success = true;
                result.Message = "📭 No Jellyseerr bridge items found to sync";
                return result;
            }
            
            _logger.LogDebug("Found {BridgeOnlyCount} Jellyseerr bridge items in folder: {BridgePath}", bridgeOnlyItems.Count, bridgeLibraryPath);
            
            // Add all bridge items to processed lists (these are all items we're working with)
            result.MoviesResult.ItemsProcessed.AddRange(bridgeMovies);
            result.ShowsResult.ItemsProcessed.AddRange(bridgeShows);
            
            // Step 2: Get all Jellyfin users and their favorites
            var allFavorites = JellyfinHelper.GetUserFavorites(_userManager, _libraryManager, _userDataManager);
            _logger.LogDebug("Retrieved favorites for {UserCount} users", allFavorites.Count);
            
            // Log all favorites for debugging
            foreach (var (user, favorites) in allFavorites)
            {
                _logger.LogTrace("User '{UserName}' has {FavoriteCount} favorites: {FavoriteNames}", 
                    user.Username, favorites.Count, 
                    string.Join(", ", favorites.Select(f => f.Name)));
            }
            
            // Add all favorited items to found lists (these are items that were favorited by users)
            var allFavoritedItems = allFavorites.Values.SelectMany(favs => favs).ToList();
            result.MoviesResult.ItemsFound.AddRange(allFavoritedItems.OfType<Movie>());
            result.ShowsResult.ItemsFound.AddRange(allFavoritedItems.OfType<Series>());
            
            // Step 3: Get all Jellyseerr users for request creation
            var jellyseerrUsers = await _bridgeService.GetJellyseerrUsersAsync();
            if (jellyseerrUsers == null || jellyseerrUsers.Count == 0)
            {
                _logger.LogWarning("No Jellyseerr users found");
                result.Success = false;
                result.Message = "👥 No Jellyseerr users found";
                return result;
            }
            
            // Step 4: Group bridge-only items by TMDB ID and find first user who favorited each
            var uniqueItemsWithJellyseerrUser = _bridgeService.EnsureFirstJellyseerrUser(bridgeOnlyItems, allFavorites, jellyseerrUsers);
            _logger.LogDebug("Found {UniqueCount} unique Jellyseerr bridge items from Jellyseerr user favorites (from {TotalCount} total)", 
                uniqueItemsWithJellyseerrUser.Count, bridgeOnlyItems.Count);
            
            // Step 5 & 6: Create requests for favorited bridge-only items
            var requestResults = await _bridgeService.RequestFavorites(uniqueItemsWithJellyseerrUser);
            
            // Add the successful requests directly to the created lists
            result.MoviesResult.ItemsCreated.AddRange(requestResults.Where(r => r.Type == JellyseerrModel.MediaType.MOVIE));
            result.ShowsResult.ItemsCreated.AddRange(requestResults.Where(r => r.Type == JellyseerrModel.MediaType.TV));
            
            result.Success = true;
            result.Message = "✅ Sync to Jellyseerr completed successfully";
            result.Details = $"Processed {bridgeOnlyItems.Count} Jellyseerr bridge items, found {allFavoritedItems.Count} favorited items, created {requestResults.Count} requests for favorited items";
            
            _logger.LogDebug("Sync to Jellyseerr completed with {ResultCount} successful requests", requestResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync to Jellyseerr");
            result.Success = false;
            result.Message = $"❌ Sync to Jellyseerr failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }
}

