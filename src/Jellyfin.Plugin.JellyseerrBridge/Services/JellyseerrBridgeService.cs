using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.Utils;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Dto;
using System.Text.Json;
using JellyfinUser = Jellyfin.Data.Entities.User;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing bridge folders and metadata.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly JellyseerrLogger<JellyseerrBridgeService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly JellyseerrApiService _apiService;

    public JellyseerrBridgeService(ILogger<JellyseerrBridgeService> logger, ILibraryManager libraryManager, IDtoService dtoService, PlaceholderVideoGenerator placeholderVideoGenerator, IUserManager userManager, IUserDataManager userDataManager, JellyseerrApiService apiService)
    {
        _logger = new JellyseerrLogger<JellyseerrBridgeService>(logger);
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _placeholderVideoGenerator = placeholderVideoGenerator;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _apiService = apiService;
    }

    #region Testing/Deprecated Methods

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// </summary>
    public async Task<(List<IJellyseerrItem> allBridgeItems, List<JellyMatch> matchedItems, List<IJellyseerrItem> unmatchedItems)> TestLibraryScanAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        try
        {
            _logger.LogDebug("Testing Jellyfin library scan functionality against bridge folder metadata...");
            
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return (new List<IJellyseerrItem>(), new List<JellyMatch>(), new List<IJellyseerrItem>());
            }

            // Read bridge folder metadata
            var (bridgeMovieMetadata, bridgeShowMetadata) = await ReadMetadataAsync();

            // Find matches between bridge folder metadata and existing Jellyfin items
            var (matchedItems, unmatchedItems) = await LibraryScanAsync(bridgeMovieMetadata, bridgeShowMetadata);

            _logger.LogTrace("Library scan test completed. Found {MovieCount} movies, {ShowCount} shows, {MatchCount} matches", 
                bridgeMovieMetadata.Count, bridgeShowMetadata.Count, matchedItems.Count);

            // Return all bridge items (not just matches) for test display purposes
            var allBridgeItems = new List<IJellyseerrItem>();
            allBridgeItems.AddRange(bridgeMovieMetadata.Cast<IJellyseerrItem>());
            allBridgeItems.AddRange(bridgeShowMetadata.Cast<IJellyseerrItem>());

            return (allBridgeItems, matchedItems, unmatchedItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return (new List<IJellyseerrItem>(), new List<JellyMatch>(), new List<IJellyseerrItem>());
        }
    }

    /// <summary>
    /// Test method to scan all users and their favorite items, plus requests from Jellyseerr.
    /// </summary>
    public async Task<(List<JellyseerrMediaRequest> requests, Dictionary<JellyfinUser, List<BaseItem>> userFavorites)> TestFavoritesScanAsync()
    {
        try
        {
            _logger.LogDebug("Testing Jellyfin favorites scan functionality with Jellyseerr requests...");
            
            // Get all Jellyfin users and their favorites
            var allFavorites = JellyfinHelper.GetUserFavorites(_userManager, _libraryManager, _userDataManager);
            
            // Fetch all requests from Jellyseerr
            try
            {
                var requestsResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.ReadRequests);
                if (requestsResult is List<JellyseerrMediaRequest> requests)
                {
                    _logger.LogDebug("Fetched {RequestCount} requests from Jellyseerr, {UserCount} users with {FavoriteCount} favorites", 
                        requests.Count, allFavorites.Count, allFavorites.Values.SelectMany(f => f).Count());
                    return (requests, allFavorites);
                }
                else
                {
                    _logger.LogWarning("Failed to fetch requests from Jellyseerr - unexpected result type");
                    return (new List<JellyseerrMediaRequest>(), allFavorites);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch requests from Jellyseerr");
                return (new List<JellyseerrMediaRequest>(), allFavorites);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during favorites scan test");
            throw;
        }
    }

    /// <summary>
    /// Test method to create requests for favorited items that are in the Jellyseerr bridge folder but not in the main Jellyfin library.
    /// </summary>
    public async Task<List<JellyseerrMediaRequest>> TestAddRequestsAsync()
    {
        try
        {
            _logger.LogDebug("Starting test request creation for favorited unmatched items");
            
            var testRequestResults = new List<JellyseerrMediaRequest>();
            
            // Step 1: Get bridge folder items and use LibraryScanAsync to get matched and unmatched items
            var (bridgeMovies, bridgeShows) = await ReadMetadataAsync();
            var (matchedItems, unmatchedItems) = await LibraryScanAsync(bridgeMovies, bridgeShows);
            
            if (unmatchedItems.Count == 0)
            {
                _logger.LogWarning("No unmatched items found in Jellyseerr bridge folder");
                return testRequestResults;
            }
            
            _logger.LogDebug("Found {UnmatchedCount} unmatched items in Jellyseerr bridge folder", unmatchedItems.Count);
            
            // Step 2: Get all Jellyfin users and their favorites
            var allFavorites = JellyfinHelper.GetUserFavorites(_userManager, _libraryManager, _userDataManager);
            
            // Step 3: Get all Jellyseerr users for request creation
            var jellyseerrUsers = await GetJellyseerrUsersAsync();
            if (jellyseerrUsers == null || jellyseerrUsers.Count == 0)
            {
                _logger.LogWarning("No Jellyseerr users found");
                return testRequestResults;
            }
            
            // Step 4: Get existing requests to avoid duplicates
            var existingRequests = await GetExistingRequestsAsync();
            
            // Step 5: Filter unmatched items to only include those that are favorited
            var favoritedUnmatchedItems = new List<IJellyseerrItem>();
            
            foreach (var unmatchedItem in unmatchedItems)
            {
                // Check if this unmatched item is favorited by any user
                var isFavorited = false;
                foreach (var (jellyfinUser, favoriteItems) in allFavorites)
                {
                    var isFavoritedByUser = favoriteItems.Any(favItem => 
                    {
                        var favTmdbId = JellyfinHelper.GetTmdbId(favItem);
                        return favTmdbId.HasValue && favTmdbId.Value == unmatchedItem.Id;
                    });
                    
                    if (isFavoritedByUser)
                    {
                        isFavorited = true;
                        break;
                    }
                }
                
                if (isFavorited)
                {
                    favoritedUnmatchedItems.Add(unmatchedItem);
                }
            }
            
            if (favoritedUnmatchedItems.Count == 0)
            {
                _logger.LogWarning("No favorited unmatched items found");
                return testRequestResults;
            }
            
            _logger.LogDebug("Found {FavoritedUnmatchedCount} favorited unmatched items", favoritedUnmatchedItems.Count);
            
            // Step 6: Create requests for each favorited unmatched item
            foreach (var favoritedItem in favoritedUnmatchedItems)
            {
                try
                {
                    _logger.LogTrace("Processing favorited unmatched item: {ItemName} (TMDB ID: {TmdbId})", 
                        favoritedItem.MediaName, favoritedItem.Id);
                    
                    // Check if already requested in Jellyseerr
                    var alreadyRequested = existingRequests.Any(req => req.Media?.Id == favoritedItem.Id);
                    if (alreadyRequested)
                    {
                        _logger.LogTrace("Skipping {ItemName} - already requested in Jellyseerr", favoritedItem.MediaName);
                    continue;
                }
                
                    // Find users who have favorited this item
                    var favoritedByUsers = new List<(JellyfinUser user, JellyseerrUser jellyseerrUser)>();
                    
                    foreach (var (jellyfinUser, favoriteItems) in allFavorites)
                    {
                        var isFavoritedByUser = favoriteItems.Any(favItem => 
                        {
                            var favTmdbId = JellyfinHelper.GetTmdbId(favItem);
                            return favTmdbId.HasValue && favTmdbId.Value == favoritedItem.Id;
                        });
                        
                        if (isFavoritedByUser)
                        {
                            // Find corresponding Jellyseerr user
                            var jellyseerrUser = jellyseerrUsers.FirstOrDefault(u => 
                                !string.IsNullOrEmpty(u.JellyfinUserId) && 
                                Guid.TryParse(u.JellyfinUserId, out var parsedId) && 
                                parsedId == jellyfinUser.Id);
                            
                            if (jellyseerrUser != null)
                            {
                                favoritedByUsers.Add((jellyfinUser, jellyseerrUser));
                            }
                        }
                    }
                    
                    if (favoritedByUsers.Count == 0)
                    {
                        _logger.LogTrace("Skipping {ItemName} - not favorited by any user", favoritedItem.MediaName);
                    continue;
                }

                    _logger.LogDebug("Found {FavoriteCount} users who favorited {ItemName}", 
                        favoritedByUsers.Count, favoritedItem.MediaName);
                    
                    // Create requests for each user who favorited this item
                    foreach (var (jellyfinUser, jellyseerrUser) in favoritedByUsers)
                    {
                        try
                        {
                            var requestParams = new Dictionary<string, object>
                            {
                                ["mediaType"] = favoritedItem.MediaType.ToString().ToLower(),
                                ["mediaId"] = favoritedItem.Id,
                                ["userId"] = jellyseerrUser.Id,
                                ["seasons"] = "all" // Only for seasons, but doesn't stop it from working for movies
                            };
                            
                            _logger.LogTrace("Creating request for {ItemName} on behalf of {UserName}", 
                                favoritedItem.MediaName, jellyfinUser.Username);
                            
                            var requestResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.CreateRequest, parameters: requestParams);
                            var request = requestResult as JellyseerrMediaRequest;
                            if (request != null)
                            {
                                testRequestResults.Add(request);
                                _logger.LogTrace("Successfully created request for {ItemName} on behalf of {UserName}", 
                                    favoritedItem.MediaName, jellyfinUser.Username);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to create request for {ItemName} on behalf of {UserName}", 
                                    favoritedItem.MediaName, jellyfinUser.Username);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to create request for {ItemName} on behalf of {UserName}", 
                                favoritedItem.MediaName, jellyfinUser.Username);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process favorited unmatched item: {ItemName}", favoritedItem.MediaName);
                }
            }
            
            _logger.LogDebug("Test request creation completed with {ResultCount} successful requests", testRequestResults.Count);
            return testRequestResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestAddRequestsAsync");
            throw;
        }
    }

    /// <summary>
    /// Create season folders for all TV shows.
    /// Creates Season 01 through Season 12 folders with season placeholder videos for each show.
    /// </summary>
    [Obsolete("This method is deprecated and will be removed in a future version.")]
    public async Task CreateSeasonFoldersForShows(List<JellyseerrShow> shows)
    {
        _logger.LogDebug("[JellyseerrBridge] CreateSeasonFoldersForShows: Starting season folder creation for {ShowCount} shows", shows.Count);
        
        foreach (var show in shows)
        {
            try
            {
                var showFolderPath = GetJellyseerrItemDirectory(show);
                
                _logger.LogTrace("[JellyseerrBridge] CreateSeasonFoldersForShows: Creating season folders for show '{MediaName}' in '{ShowFolderPath}'", 
                    show.MediaName, showFolderPath);
                
                var seasonFolderName = "Season 01";
                var seasonFolderPath = Path.Combine(showFolderPath, seasonFolderName);
                
                try
                {
                    // Create season folder if it doesn't exist
                    if (!Directory.Exists(seasonFolderPath))
                    {
                        Directory.CreateDirectory(seasonFolderPath);
                        _logger.LogDebug("[JellyseerrBridge] CreateSeasonFoldersForShows: Created season folder: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    // Generate season placeholder video
                    var placeholderSuccess = await _placeholderVideoGenerator.GeneratePlaceholderSeasonAsync(seasonFolderPath);
                    if (placeholderSuccess)
                    {
                        _logger.LogDebug("[JellyseerrBridge] CreateSeasonFoldersForShows: Created season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    else
                    {
                        _logger.LogWarning("[JellyseerrBridge] CreateSeasonFoldersForShows: Failed to create season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    _logger.LogTrace("[JellyseerrBridge] CreateSeasonFoldersForShows: ✅ Created season folder for show '{MediaName}'", show.MediaName);
        }
        catch (Exception ex)
        {
                    _logger.LogError(ex, "[JellyseerrBridge] CreateSeasonFoldersForShows: Error creating season folder for show '{MediaName}'", show.MediaName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreateSeasonFoldersForShows: ❌ ERROR creating season folders for show '{MediaName}'", show.MediaName);
            }
        }
        
        _logger.LogDebug("[JellyseerrBridge] CreateSeasonFoldersForShows: Completed season folder creation for {ShowCount} shows", shows.Count);
    }

    #endregion

    #region Writing File/Folder Data

    /// <summary>
    /// Find matches between existing Jellyfin items and bridge metadata.
    /// </summary>
    private List<JellyMatch> FindMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> existingItems, 
        List<TJellyseerr> bridgeMetadata) 
        where TJellyfin : BaseItem 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var matches = new List<JellyMatch>();

        foreach (var existingItem in existingItems)
        {
            _logger.LogDebug("[JellyseerrBridge] Checking existing item: {ItemName} (Id: {ItemId})", 
                existingItem.Name, existingItem.Id);
            
            // Safety check: Skip items that are already in the Jellyseerr library directory
            if (string.IsNullOrEmpty(existingItem.Path) || 
                JellyseerrFolderUtils.IsPathInSyncDirectory(existingItem.Path))
            {
                _logger.LogDebug("[JellyseerrBridge] Skipping item {ItemName} - already in Jellyseerr library directory: {ItemPath}", 
                    existingItem.Name, existingItem.Path);
                continue;
            }
            
            // Use the custom EqualsItem implementation rather than Equals cause I don't trust compile-time resolution.
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => bm.EqualsItem(existingItem));

            if (bridgeMatch != null)
            {
                _logger.LogTrace("[JellyseerrBridge] Found match: '{BridgeMediaName}' (Id: {BridgeId}) matches '{ExistingName}' (Id: {ExistingId})", 
                    bridgeMatch.MediaName, bridgeMatch.Id, existingItem.Name, existingItem.Id);
                
                matches.Add(new JellyMatch(bridgeMatch, existingItem));
            }
        }

        _logger.LogDebug("[JellyseerrBridge] Found {MatchCount} matches between Jellyfin items and bridge metadata", matches.Count);
        return matches;
    }

    /// <summary>
    /// Create ignore files for matched items.
    /// </summary>
    public async Task CreateIgnoreFilesAsync(List<JellyMatch> matches)
    {
        var ignoreFileTasks = new List<Task>();

        foreach (var match in matches)
        {
            var bridgeFolderPath = GetJellyseerrItemDirectory(match.JellyseerrItem);
            var item = match.JellyfinItem;
        try
        {
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            
            _logger.LogTrace("[JellyseerrBridge] Creating ignore file for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}", 
                item.Name, item.Id, ignoreFilePath);
            
            // Use DtoService to get a proper BaseItemDto with all metadata
            var dtoOptions = new DtoOptions(); // Default constructor includes all fields
            var itemDto = _dtoService.GetBaseItemDto(item, dtoOptions);
            
            _logger.LogTrace("[JellyseerrBridge] Successfully created BaseItemDto for {ItemName} - DTO has {PropertyCount} properties", 
                item.Name, itemDto?.GetType().GetProperties().Length ?? 0);
            
            var itemJson = JsonSerializer.Serialize(itemDto, new JsonSerializerOptions {
                WriteIndented = true
            });
            
            _logger.LogTrace("[JellyseerrBridge] Successfully serialized {ItemName} to JSON - JSON length: {JsonLength} characters", 
                item.Name, itemJson?.Length ?? 0);

            await File.WriteAllTextAsync(ignoreFilePath, itemJson);
            _logger.LogTrace("[JellyseerrBridge] Created ignore file for {ItemName} in {BridgeFolder}", item.Name, bridgeFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error creating ignore file for {ItemName}", item.Name);
        }
    }

        // Await all ignore file creation tasks
        await Task.WhenAll(ignoreFileTasks);
    }

    /// <summary>
    /// Get all Jellyseerr users to map with Jellyfin users.
    /// </summary>
    private async Task<List<JellyseerrUser>> GetJellyseerrUsersAsync()
    {
        try
        {
            var usersResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList);
            if (usersResult is List<JellyseerrUser> users)
            {
                _logger.LogDebug("Fetched {UserCount} users from Jellyseerr", users.Count);
                return users;
            }
            else
            {
                _logger.LogWarning("Failed to fetch users from Jellyseerr - unexpected result type");
                return new List<JellyseerrUser>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch users from Jellyseerr");
            return new List<JellyseerrUser>();
        }
    }

    /// <summary>
    /// Get existing requests from Jellyseerr to avoid duplicates.
    /// </summary>
    private async Task<List<JellyseerrMediaRequest>> GetExistingRequestsAsync()
    {
            try
            {
                var requestsResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.ReadRequests);
                if (requestsResult is List<JellyseerrMediaRequest> requests)
                {
                _logger.LogDebug("Fetched {RequestCount} existing requests from Jellyseerr", requests.Count);
                return requests;
                }
                else
                {
                    _logger.LogWarning("Failed to fetch requests from Jellyseerr - unexpected result type");
                return new List<JellyseerrMediaRequest>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch requests from Jellyseerr");
            return new List<JellyseerrMediaRequest>();
        }
    }
    
    
    



    /// <summary>
    /// Read all metadata files from the bridge folder, detecting movie vs show based on NFO files.
    /// </summary>
    private async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadMetadataAsync()
    {
        var movies = new List<JellyseerrMovie>();
        var shows = new List<JellyseerrShow>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            var directories = Directory.GetDirectories(syncDirectory, "*", SearchOption.TopDirectoryOnly);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogDebug("[JellyseerrBridge] Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                        
                        // Check for movie.nfo to identify movie folders
                        var movieNfoFile = Path.Combine(directory, "movie.nfo");
                        var showNfoFile = Path.Combine(directory, "tvshow.nfo");
                        
                        if (File.Exists(movieNfoFile))
                        {
                            // This is a movie folder
                            var movie = JellyseerrJsonSerializer.Deserialize<JellyseerrMovie>(json);
                            if (movie != null)
                            {
                                _logger.LogTrace("[JellyseerrBridge] Successfully deserialized movie - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                    movie.MediaName, movie.Id, movie.MediaType, movie.Year);
                                movies.Add(movie);
                            }
                            else
                            {
                                _logger.LogWarning("[JellyseerrBridge] Failed to deserialize movie from {MetadataFile}", metadataFile);
                            }
                        }
                        else if (File.Exists(showNfoFile))
                        {
                            // This is a show folder
                            var show = JellyseerrJsonSerializer.Deserialize<JellyseerrShow>(json);
                            if (show != null)
                            {
                                _logger.LogTrace("[JellyseerrBridge] Successfully deserialized show - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                    show.MediaName, show.Id, show.MediaType, show.Year);
                                shows.Add(show);
                            }
                            else
                            {
                                _logger.LogWarning("[JellyseerrBridge] Failed to deserialize show from {MetadataFile}", metadataFile);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[JellyseerrBridge] No NFO file found in directory {Directory} - skipping", directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyseerrBridge] Error reading metadata file: {MetadataFile}", metadataFile);
                    }
                }
            }

            _logger.LogDebug("[JellyseerrBridge] Read {MovieCount} movies and {ShowCount} shows from bridge folders", 
                movies.Count, shows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error reading metadata from {SyncDirectory}", syncDirectory);
        }

        return (movies, shows);
    }

    /// <summary>
    /// Write metadata for a single item to the appropriate folder.
    /// </summary>
    private async Task<bool> WriteMetadataAsync<TJellyseerr>(TJellyseerr item) where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        try
        {
            var targetDirectory = GetJellyseerrItemDirectory(item);

            // Create directory if it doesn't exist
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                _logger.LogDebug("[JellyseerrBridge] Created directory: {TargetDirectory}", targetDirectory);
            }

            // Set CreatedDate to current time when writing
            item.CreatedDate = DateTimeOffset.Now;
            
            // Write JSON metadata - serialize as concrete type to preserve JSON attributes
            var json = JellyseerrJsonSerializer.Serialize(item);
            
            var metadataFile = Path.Combine(targetDirectory, "metadata.json");
            await File.WriteAllTextAsync(metadataFile, json);
            _logger.LogDebug("[JellyseerrBridge] Wrote metadata to {MetadataFile}", metadataFile);
            
            // Write XML metadata only if NFO file doesn't exist
            var xmlFile = Path.Combine(targetDirectory, item.GetNfoFilename());
            if (!File.Exists(xmlFile))
            {
                var xmlText = item.ToXmlString();
                await File.WriteAllTextAsync(xmlFile, xmlText);
                _logger.LogDebug("[JellyseerrBridge] Wrote XML to {XmlFile}", xmlFile);
            }
            else
            {
                _logger.LogTrace("[JellyseerrBridge] Skipped writing XML to {XmlFile} - file already exists", xmlFile);
            }
            
            return true;
                }
                catch (Exception ex)
                {
            _logger.LogError(ex, "[JellyseerrBridge] Error writing metadata for {ItemMediaName}", item.MediaName);
            return false;
        }
    }

    /// <summary>
    /// Get the directory path for a specific item.
    /// </summary>
    private string GetJellyseerrItemDirectory(IJellyseerrItem? item = null)
    {
        if (item == null)
        {
            return JellyseerrFolderUtils.GetBaseDirectory();
        }
        var folderName = JellyseerrFolderUtils.SanitizeFileName(item.ToString() ?? string.Empty);
        return Path.Combine(JellyseerrFolderUtils.GetBaseDirectory(), folderName);
    }

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// Returns matched items as JellyMatch objects and unmatched items as IJellyseerrItem lists.
    /// </summary>
    public async Task<(List<JellyMatch> matched, List<IJellyseerrItem> unmatched)> LibraryScanAsync(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)
    {
        // Combine all items for processing
        var allItems = new List<IJellyseerrItem>();
        allItems.AddRange(movies.Cast<IJellyseerrItem>());
        allItems.AddRange(shows.Cast<IJellyseerrItem>());
        
        _logger.LogDebug("Excluding main libraries from JellyseerrBridge");
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));

        if (excludeFromMainLibraries) {
            try
            {
                if (!Directory.Exists(syncDirectory))
                {
                    throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
                }

                _logger.LogTrace("Scanning Jellyfin library for {MovieCount} movies and {ShowCount} shows", movies.Count, shows.Count);

                // Get existing Jellyfin items
                var existingMovies = JellyfinHelper.GetExistingItems<Movie>(_libraryManager);
                var existingShows = JellyfinHelper.GetExistingItems<Series>(_libraryManager);

                // Find matches between Jellyfin items and bridge metadata
                var movieMatches = FindMatches(existingMovies, movies);
                var showMatches = FindMatches(existingShows, shows);

                // Combine all matches into a single list
                var allMatches = new List<JellyMatch>();
                allMatches.AddRange(movieMatches);
                allMatches.AddRange(showMatches);

                // Filter unmatched items by excluding matched ones
                var matchedIds = allMatches.Select(m => m.JellyseerrItem.Id).ToHashSet();
                var unmatchedItems = allItems.Where(item => !matchedIds.Contains(item.Id)).ToList();

                _logger.LogDebug("Library scan completed. Matched {MatchedMovieCount}/{MovieCount} movies + {MatchedShowCount}/{ShowCount} shows = {TotalCount} total. Unmatched: {UnmatchedCount} items", 
                    movieMatches.Count, movies.Count, showMatches.Count, shows.Count, allMatches.Count, unmatchedItems.Count);

                return (allMatches, unmatchedItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during library scan test");
            }
        } else {
            _logger.LogDebug("Including main libraries in JellyseerrBridge");
            
            // Delete all existing .ignore files when including main libraries
            var deletedCount = await DeleteAllIgnoreFilesAsync();
            _logger.LogTrace("Deleted {DeletedCount} .ignore files from JellyseerrBridge", deletedCount);
        } 
        return (new List<JellyMatch>(), allItems);
    }

    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows using JellyseerrFolderManager.
    /// </summary>
    public async Task<ProcessResult> CreateFoldersAsync<TJellyseerr>(List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var result = new ProcessResult();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogDebug("[JellyseerrBridge] CreateFoldersAsync: Starting folder creation for {ItemType} - Base Directory: {BaseDirectory}, Items Count: {ItemCount}", 
            typeof(TJellyseerr).Name, baseDirectory, items.Count);
        
        
        // Add all items to processed list at once
        result.ItemsProcessed.AddRange(items);
        
        foreach (var item in items)
        {
            try
            {
                _logger.LogTrace("[JellyseerrBridge] CreateFoldersAsync: Processing item {ItemNumber}/{TotalItems} - MediaName: '{MediaName}', Id: {Id}, Year: '{Year}'", 
                    result.Processed, items.Count, item.MediaName, item.Id, item.Year);
                
                // Generate folder name and get directory path
                var folderName = GetJellyseerrItemDirectory(item);
                var folderExists = Directory.Exists(folderName);

                _logger.LogTrace("[JellyseerrBridge] CreateFoldersAsync: Folder details - Name: '{FolderName}', Exists: {FolderExists}", 
                    folderName, folderExists);

                // Write metadata using folder manager
                var success = await WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderExists)
                    {
                        result.ItemsUpdated.Add(item);
                        _logger.LogTrace("[JellyseerrBridge] CreateFoldersAsync: ✅ UPDATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                    else
                    {
                        result.ItemsAdded.Add(item);
                        _logger.LogTrace("[JellyseerrBridge] CreateFoldersAsync: ✅ CREATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                }
                else
                {
                    _logger.LogError("[JellyseerrBridge] CreateFoldersAsync: ❌ FAILED to create folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                        item, item.MediaName, item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreateFoldersAsync: ❌ ERROR creating folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                    item, item.MediaName, item.Id);
            }
        }
        
        _logger.LogDebug("[JellyseerrBridge] CreateFoldersAsync: Completed folder creation for {ItemType} - Processed: {Processed}, Created: {Created}, Updated: {Updated}", 
            typeof(TJellyseerr).Name, result.Processed, result.Created, result.Updated);
        
        return result;
    }
    
    /// <summary>
    /// Creates placeholder videos for the provided unmatched items.
    /// </summary>
    public async Task<List<TJellyseerr>> CreatePlaceholderVideosAsync<TJellyseerr>(
        List<TJellyseerr> unmatchedItems) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var processedItems = new List<TJellyseerr>();
        var tasks = new List<Task>();
        
        _logger.LogDebug("[JellyseerrBridge] CreatePlaceholderVideosAsync: Processing {UnmatchedCount} unmatched items for placeholder creation", 
            unmatchedItems.Count);
        
        foreach (var item in unmatchedItems)
        {
            try
            {
                // Get the folder path for this item
                var folderPath = GetJellyseerrItemDirectory(item);
                
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    throw new InvalidOperationException($"Folder does not exist for item: {item.MediaName}");
                }
                
                // Create placeholder video based on media type
                if (item is JellyseerrMovie)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderMovieAsync(folderPath));
                }
                else if (item is JellyseerrShow)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderShowAsync(folderPath));
                }
                
                processedItems.Add(item);
                _logger.LogTrace("[JellyseerrBridge] CreatePlaceholderVideosAsync: ✅ Created placeholder video for {ItemName}", item.MediaName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreatePlaceholderVideosAsync: ❌ ERROR creating placeholder video for {ItemName}", item.MediaName);
            }
        }
        
        // Await all placeholder video tasks
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("[JellyseerrBridge] CreatePlaceholderVideosAsync: Completed - Processed {ProcessedCount} items", 
            processedItems.Count);
        
        return processedItems;
    }

    /// <summary>
    /// Helper method to process items for cleanup.
    /// </summary>
    private List<TJellyseerr> ProcessItemsForCleanup<TJellyseerr>(
        List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var deletedItems = new List<TJellyseerr>();
        var maxCollectionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxCollectionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxCollectionDays);
        var itemType = typeof(TJellyseerr).Name.ToLower().Replace("jellyseerr", "");
        
        _logger.LogTrace("[JellyseerrBridge] ProcessItemsForCleanup: Processing {ItemCount} {ItemType}s for cleanup (older than {MaxCollectionDays} days, before {CutoffDate})", 
            items.Count, itemType, maxCollectionDays, cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        
        try
        {
            foreach (var item in items)
            {
                // Check if the item's CreatedDate is older than the cutoff date
                // Treat null CreatedDate as very old (past cutoff date)
                if (item.CreatedDate?.DateTime < cutoffDate || item.CreatedDate == null)
                {
                    _logger.LogTrace("[JellyseerrBridge] ProcessItemsForCleanup: Marking {ItemType} for removal - {ItemName} (Created: {CreatedDate})", 
                        itemType, item.MediaName, item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    
                    var itemDirectory = GetJellyseerrItemDirectory(item);
                    
                    if (Directory.Exists(itemDirectory))
                    {
                        Directory.Delete(itemDirectory, true);
                        deletedItems.Add(item);
                        _logger.LogTrace("[JellyseerrBridge] ProcessItemsForCleanup: ✅ Removed old {ItemType} '{ItemName}' (Created: {CreatedDate})", 
                            itemType, item.MediaName, item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] ProcessItemsForCleanup: Error processing {ItemType}", itemType);
        }
        
        return deletedItems;
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Recursively deletes all .ignore files from the Jellyseerr bridge directory.
    /// </summary>
    private Task<int> DeleteAllIgnoreFilesAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return Task.FromResult(0);
            }

            _logger.LogTrace("Starting recursive deletion of .ignore files from: {SyncDirectory}", syncDirectory);
            
            var deletedCount = 0;
            var ignoreFiles = Directory.GetFiles(syncDirectory, ".ignore", SearchOption.AllDirectories);
            
            foreach (var ignoreFile in ignoreFiles)
            {
                try
                {
                    File.Delete(ignoreFile);
                    deletedCount++;
                    _logger.LogDebug("Deleted .ignore file: {IgnoreFile}", ignoreFile);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete .ignore file: {IgnoreFile}", ignoreFile);
                }
            }

            _logger.LogTrace("Completed deletion of .ignore files. Deleted {DeletedCount} files out of {TotalCount} found", 
                deletedCount, ignoreFiles.Length);
            
            return Task.FromResult(deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during .ignore file deletion");
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Cleans up metadata by removing items older than the specified number of days.
    /// </summary>
    public async Task<ProcessResult> CleanupMetadataAsync()
    {
        var result = new ProcessResult();

        var maxCollectionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxCollectionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxCollectionDays);

        try
        {
            // Read all bridge folder metadata
            var (movies, shows) = await ReadMetadataAsync();
            
            _logger.LogDebug("[JellyseerrBridge] CleanupMetadataAsync: Found {MovieCount} movies and {ShowCount} shows to check for cleanup", 
                movies.Count, shows.Count);

            // Process movies and shows using the same logic
            var deletedMovies = ProcessItemsForCleanup(movies);
            var deletedShows = ProcessItemsForCleanup(shows);
            
            // Create ProcessResult from the results
            result.ItemsProcessed.AddRange(movies);
            result.ItemsProcessed.AddRange(shows);
            result.ItemsDeleted.AddRange(deletedMovies);
            result.ItemsDeleted.AddRange(deletedShows);

            _logger.LogDebug("[JellyseerrBridge] CleanupMetadataAsync: Completed cleanup - {Result}", result.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] CleanupMetadataAsync: Error during cleanup process");
        }

        return result;
    }

    #endregion

}
