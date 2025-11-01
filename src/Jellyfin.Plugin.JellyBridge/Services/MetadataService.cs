using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing metadata files and folder operations for Jellyseerr bridge items.
/// </summary>
public class MetadataService
{
    private readonly DebugLogger<MetadataService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinIUserManager _userManager;

    public MetadataService(ILogger<MetadataService> logger, JellyfinILibraryManager libraryManager, JellyfinIUserDataManager userDataManager, JellyfinIUserManager userManager)
    {
        _logger = new DebugLogger<MetadataService>(logger);
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
    }

    /// <summary>
    /// Read all metadata files from the bridge folder, detecting movie vs show based on NFO files.
    /// </summary>
    public async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadMetadataAsync()
    {
        var movies = new List<JellyseerrMovie>();
        var shows = new List<JellyseerrShow>();

        try
        {
            // Get categorized directories
            var (movieDirectories, showDirectories) = ReadMetadataInternal();

            // Parse all movie directories
            foreach (var directory in movieDirectories)
                {
                    try
                    {
                    var metadataFile = Path.Combine(directory, IJellyseerrItem.GetMetadataFilename());
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogTrace("Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                        
                            var movie = JellyBridgeJsonSerializer.Deserialize<JellyseerrMovie>(json);
                            if (movie != null)
                            {
                                _logger.LogTrace("Successfully deserialized movie - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                    movie.MediaName, movie.Id, movie.MediaType, movie.Year);
                                movies.Add(movie);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to deserialize movie from {MetadataFile}", metadataFile);
                            }
                        }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading metadata file from directory: {Directory}", directory);
                }
            }

            // Parse all show directories
            foreach (var directory in showDirectories)
            {
                try
                {
                    var metadataFile = Path.Combine(directory, IJellyseerrItem.GetMetadataFilename());
                    var json = await File.ReadAllTextAsync(metadataFile);
                    _logger.LogTrace("Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                    
                            var show = JellyBridgeJsonSerializer.Deserialize<JellyseerrShow>(json);
                            if (show != null)
                            {
                                _logger.LogTrace("Successfully deserialized show - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                    show.MediaName, show.Id, show.MediaType, show.Year);
                                shows.Add(show);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to deserialize show from {MetadataFile}", metadataFile);
                        }
                    }
                    catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "Error reading metadata file from directory: {Directory}", directory);
                }
            }

            _logger.LogDebug("Read {MovieCount} movies and {ShowCount} shows from bridge folders", 
                movies.Count, shows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading metadata from bridge folders");
        }

        return (movies, shows);
    }

    /// <summary>
    /// Internal method to discover and categorize directories containing metadata files.
    /// </summary>
    /// <returns>Tuple containing lists of movie directories and show directories</returns>
    private (List<string> movieDirectories, List<string> showDirectories) ReadMetadataInternal()
    {
        var movieDirectories = new List<string>();
        var showDirectories = new List<string>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            // Get all subdirectories that contain metadata files
            var metadataFiles = Directory.GetFiles(syncDirectory, IJellyseerrItem.GetMetadataFilename(), SearchOption.AllDirectories);
            
            foreach (var metadataFile in metadataFiles)
            {
                var directory = Path.GetDirectoryName(metadataFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    // Check for movie.nfo to identify movie folders
                    var movieNfoFile = Path.Combine(directory, JellyseerrMovie.GetNfoFilename());
                    var showNfoFile = Path.Combine(directory, JellyseerrShow.GetNfoFilename());
                    
                    if (File.Exists(movieNfoFile))
                    {
                        movieDirectories.Add(directory);
                    }
                    else if (File.Exists(showNfoFile))
                    {
                        showDirectories.Add(directory);
                    }
                    else
                    {
                        _logger.LogWarning("No NFO file found in directory {Directory} - skipping", directory);
                    }
                }
            }

            _logger.LogDebug("Found {MovieCount} movie directories and {ShowCount} show directories", 
                movieDirectories.Count, showDirectories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering metadata directories from {SyncDirectory}", syncDirectory);
        }

        return (movieDirectories, showDirectories);
    }

    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows using JellyseerrFolderManager.
    /// </summary>
    public async Task<(List<TJellyseerr> added, List<TJellyseerr> updated)> CreateFolderMetadataAsync<TJellyseerr>(List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var addedItems = new List<TJellyseerr>();
        var updatedItems = new List<TJellyseerr>();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogDebug("Starting folder creation for {ItemType} - Base Directory: {BaseDirectory}, Items Count: {ItemCount}", 
            typeof(TJellyseerr).Name, baseDirectory, items.Count);
        
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                _logger.LogTrace("Processing item {ItemNumber}/{TotalItems} - MediaName: '{MediaName}', Id: {Id}, Year: '{Year}'", 
                    i + 1, items.Count, item.MediaName, item.Id, item.Year);
                
                // Generate folder name and get directory path
                var folderName = GetJellyseerrItemDirectory(item);
                var folderExists = Directory.Exists(folderName);

                _logger.LogTrace("Folder details - Name: '{FolderName}', Exists: {FolderExists}", 
                    folderName, folderExists);

                // Write metadata using folder manager
                var success = await WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderExists)
                    {
                        updatedItems.Add(item);
                        _logger.LogTrace("✅ UPDATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                    else
                    {
                        addedItems.Add(item);
                        _logger.LogTrace("✅ CREATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                }
                else
                {
                    _logger.LogError("❌ FAILED to create folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                        item, item.MediaName, item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR creating folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                    item, item.MediaName, item.Id);
            }
        }
        
        _logger.LogDebug("Completed folder creation for {ItemType} - Added: {Added}, Updated: {Updated}", 
            typeof(TJellyseerr).Name, addedItems.Count, updatedItems.Count);
        
        return (addedItems, updatedItems);
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
                _logger.LogDebug("Created directory: {TargetDirectory}", targetDirectory);
            }

            // Set CreatedDate to current time when writing
            item.CreatedDate = DateTimeOffset.Now;
            
            // Write JSON metadata - serialize as concrete type to preserve JSON attributes
            var json = JellyBridgeJsonSerializer.Serialize(item);
            
            var metadataFile = Path.Combine(targetDirectory, IJellyseerrItem.GetMetadataFilename());
            await File.WriteAllTextAsync(metadataFile, json);
            _logger.LogTrace("Wrote metadata to {MetadataFile}", metadataFile);
            
            // Write XML metadata only if NFO file doesn't exist
            var xmlFile = Path.Combine(targetDirectory, IJellyseerrItem.GetNfoFilename(item));
            if (!File.Exists(xmlFile))
            {
                var xmlText = item.ToXmlString();
                await File.WriteAllTextAsync(xmlFile, xmlText);
                _logger.LogTrace("Wrote XML to {XmlFile}", xmlFile);
            }
            else
            {
                _logger.LogTrace("Skipped writing XML to {XmlFile} - file already exists", xmlFile);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing metadata for {ItemMediaName}", item.MediaName);
            return false;
        }
    }


    #region Helpers

    /// <summary>
    /// Get the directory path for a specific item.
    /// </summary>
    public string GetJellyseerrItemDirectory(IJellyseerrItem? item = null)
    {
        if (item == null)
        {
            return FolderUtils.GetBaseDirectory();
        }
        var itemString = item.ToString();
        if (string.IsNullOrEmpty(itemString))
        {
            throw new ArgumentException($"Item {item.GetType().Name} returned null or empty string from ToString()", nameof(item));
        }
        var itemFolder = FolderUtils.SanitizeFileName(itemString);
        if(Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.CreateSeparateLibraries)) && !string.IsNullOrEmpty(item.NetworkTag))
        {
            var networkPrefix = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryPrefix));
            var networkFolder = FolderUtils.SanitizeFileName(networkPrefix + item.NetworkTag);
            return Path.Combine(FolderUtils.GetBaseDirectory(), networkFolder, itemFolder);
        }
        // If not using network prefix, just store in the base directory with the folder name
        return Path.Combine(FolderUtils.GetBaseDirectory(), itemFolder);
    }

    /// <summary>
    /// Randomizes play counts for all discover library items across all users.
    /// This enables random sorting by play count in Jellyfin.
    /// Uses ReadMetadataInternal to discover movie and show directories.
    /// </summary>
    /// <returns>A tuple containing a list of successful updates (name, type, playCount), a list of failed item paths, and a list of skipped item paths (ignored files).</returns>
    public async Task<(List<(string name, string type, int playCount)> successes, List<string> failures, List<string> skipped)> RandomizePlayCountAsync()
    {
        var successes = new List<(string name, string type, int playCount)>();
        var failures = new List<string>();
        var skipped = new List<string>();
        
        try
        {
            // Get categorized directories
            var (movieDirectories, showDirectories) = ReadMetadataInternal();
            var totalCount = movieDirectories.Count + showDirectories.Count;

            if (totalCount == 0)
            {
                _logger.LogDebug("No directories found to update");
                return (successes, failures, skipped);
            }

            // Get all users
            var users = _userManager.GetAllUsers().ToList();
            if (users.Count == 0)
            {
                _logger.LogWarning("No users found - cannot update play counts");
                return (successes, failures, skipped);
            }

            // Create a list of play count values (1000, 1100, 1200, etc. with increments of 100) and shuffle them
            // Using increments of 100 ensures that when users play items (incrementing by 1), the sort order remains stable
            var random = System.Random.Shared;
            var playCounts = Enumerable.Range(0, totalCount)
                .Select(i => 1000 + (i * 100))
                .OrderBy(_ => random.Next())
                .ToList();

            // Create directory info map with play count and isShow flag (for efficient lookup)
            // Combine movies and shows, then map each to (playCount, isShow) tuple
            var directoryInfoMap = movieDirectories.Select(dir => (dir, isShow: false))
                .Concat(showDirectories.Select(dir => (dir, isShow: true)))
                .Select((item, index) => (item.dir, playCount: playCounts[index], item.isShow))
                .ToDictionary(x => x.dir, x => (x.playCount, x.isShow));

            // Update play count for each item across all users - parallelize by item
            var updateTasks = directoryInfoMap.Select(async kvp =>
            {
                var directory = kvp.Key;
                var (assignedPlayCount, isShowDirectory) = kvp.Value;
                
                try
                {
                    // Check if directory is ignored (has .ignore file)
                    var ignoreFile = Path.Combine(directory, ".ignore");
                    if (File.Exists(ignoreFile))
                    {
                        _logger.LogDebug("Item ignored (has .ignore file) for path: {Path}", directory);
                        return (success: ((string name, string type, int playCount)?)null, failure: (string?)null, skipped: directory);
                    }

                    // Find item by directory path - handles both movies and shows
                    var item = _libraryManager.FindItemByDirectoryPath(directory);
                    
                    if (item == null)
                    {
                        _logger.LogDebug("Item not found for path: {Path}", directory);
                        return (success: ((string name, string type, int playCount)?)null, failure: directory, skipped: (string?)null);
                    }
                    string itemName = item.Name;
                    string itemType = item.GetType().Name;
                    
                    // Update play count for each user - parallelize user updates for this item
                    var userUpdateTasks = users.Select(user => Task.Run(() =>
                    {
                        try
                        {
                            if (_userDataManager.TryUpdatePlayCount(user, item, assignedPlayCount))
                            {
                                _logger.LogTrace("Updated play count for user {UserName}, item: {ItemName} ({Path}) to {PlayCount}", 
                                    user.Username, itemName, directory, assignedPlayCount);
                                
                                // For shows, also set play count to 1 for placeholder episode if it exists and has play count 0
                                if (isShowDirectory)
                                {
                                    try
                                    {
                                        var placeholderPath = PlaceholderVideoGenerator.GetSeasonPlaceholderPath(directory);
                                        if (File.Exists(placeholderPath))
                                        {
                                            var episode = _libraryManager.Inner.FindByPath(placeholderPath, isFolder: false);
                                            if (episode != null)
                                            {
                                                var userData = _userDataManager.GetUserData(user, episode);
                                                if (userData != null && userData.PlayCount == 0)
                                                {
                                                    if (_userDataManager.TryUpdatePlayCount(user, episode, 1))
                                                    {
                                                        _logger.LogTrace("Set play count to 1 for placeholder episode '{EpisodeName}' for user {UserName}", 
                                                            episode.Name, user.Username);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Failed to set placeholder episode play count for user {UserName}, show: {Directory}", user.Username, directory);
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Failed to update play count for user {UserName}, item: {Path}", user.Username, directory);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("Operation canceled while updating play count for user {UserName}, item: {Path}", user.Username, directory);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update play count for user {UserName}, item: {Path}", user.Username, directory);
                        }
                    }));

                    // Wait for all user updates for this item to complete
                    await Task.WhenAll(userUpdateTasks);

                    return (success: ((string name, string type, int playCount)?)(itemName, itemType, assignedPlayCount), failure: (string?)null, skipped: (string?)null);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Operation canceled while processing directory: {Directory}", directory);
                    return (success: ((string name, string type, int playCount)?)null, failure: directory, skipped: (string?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update play count for directory: {Directory}", directory);
                    return (success: ((string name, string type, int playCount)?)null, failure: directory, skipped: (string?)null);
                }
            });

            // Wait for all item updates to complete and collect results
            var results = await Task.WhenAll(updateTasks);
            
            foreach (var (success, failure, skippedItem) in results)
            {
                if (success.HasValue)
                {
                    successes.Add((success.Value.name, success.Value.type, success.Value.playCount));
                }
                else if (failure != null)
                {
                    failures.Add(failure);
                }
                else if (skippedItem != null)
                {
                    skipped.Add(skippedItem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating play counts");
        }
        
        return (successes, failures, skipped);
    }

    #endregion
}