using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for handling file creation and folder management in the JellyBridge library.
/// </summary>
public class MetadataService
{
    private readonly DebugLogger<MetadataService> _logger;

    public MetadataService(ILogger<MetadataService> logger)
    {
        _logger = new DebugLogger<MetadataService>(logger);
    }

    public async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadMetadataAsync()
    {
        // Get categorized directories
        var (movieDirectories, showDirectories) = ReadMetadataFolders();
        // Read metadata
        return await ReadMetadataAsync(movieDirectories, showDirectories);
    }

    /// <summary>
    /// Read all metadata files from the bridge folder, detecting movie vs show based on NFO files.
    /// </summary>
    public async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadMetadataAsync(List<string> movieDirectories, List<string> showDirectories)
    {
        var movies = new List<JellyseerrMovie>();
        var shows = new List<JellyseerrShow>();

        try
        {

            // Parse all movie directories
            foreach (var directory in movieDirectories)
                {
                    try
                    {
                    var metadataFile = Path.Combine(directory, IJellyseerrItem.GetMetadataFilename());
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogTrace("Reading metadata from {MetadataFile}", metadataFile);
                        
                            var movie = JellyBridgeJsonSerializer.Deserialize<JellyseerrMovie>(json);
                            var hasMeaningfulData = (movie?.Id != null && movie?.Id > 0) || !string.IsNullOrWhiteSpace(movie?.MediaName);
                            if (movie != null && hasMeaningfulData)
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
                    _logger.LogTrace("Reading metadata from {MetadataFile}", metadataFile);
                    
                            var show = JellyBridgeJsonSerializer.Deserialize<JellyseerrShow>(json);
                            var hasMeaningfulData = (show?.Id != null && show?.Id > 0) || !string.IsNullOrWhiteSpace(show?.MediaName);
                            if (show != null && hasMeaningfulData)
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

    public (List<string> movieDirectories, List<string> showDirectories) ReadMetadataFolders()
    {
        var syncDirectory = FolderUtils.GetBaseDirectory();

        return ReadMetadataFolders(syncDirectory);
    }

    /// <summary>
    /// Discovers and categorizes directories containing metadata files.
    /// </summary>
    /// <returns>Tuple containing lists of movie directories and show directories</returns>
    public (List<string> movieDirectories, List<string> showDirectories) ReadMetadataFolders(string folderPath)
    {
        var movieDirectories = new List<string>();
        var showDirectories = new List<string>();

        try
        {
            folderPath = FolderUtils.GetExistingFolderOrThrow(folderPath);

            // Get all subdirectories that contain metadata files
            var metadataFiles = Directory.GetFiles(folderPath, IJellyseerrItem.GetMetadataFilename(), SearchOption.AllDirectories);
            
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
            _logger.LogError(ex, "Error discovering metadata directories from {FolderPath}", folderPath);
        }

        return (movieDirectories, showDirectories);
    }

    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows using JellyseerrFolderManager.
    /// </summary>
    public async Task<(List<IJellyseerrItem> added, List<IJellyseerrItem> updated)> CreateFolderMetadataAsync(List<IJellyseerrItem> items)
    {
        var addedItems = new List<IJellyseerrItem>();
        var updatedItems = new List<IJellyseerrItem>();
        
        // Get configuration values using centralized helper
        var baseDirectory = FolderUtils.GetBaseDirectory();
        
        _logger.LogDebug("Starting folder creation for mixed media - Base Directory: {BaseDirectory}, Items Count: {ItemCount}", 
            baseDirectory, items.Count);
        
        // Local async function to process a single item (write metadata and handle result)
        async Task ProcessCreateFolderMetadataAsync(IJellyseerrItem item, int index)
        {
            try
            {
                _logger.LogTrace("Processing item {ItemNumber}/{TotalItems} - MediaName: '{MediaName}', Id: {Id}, Year: '{Year}'", 
                    index + 1, items.Count, item.MediaName, item.Id, item.Year);
                
                // Generate folder name and get directory path
                var folderName = GetJellyBridgeItemDirectory(item);
                var folderAlreadyExists = FolderUtils.FolderExistsThrowNull(folderName);

                // Write metadata
                var success = await WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderAlreadyExists)
                    {
                        updatedItems.Add(item);
                        _logger.LogTrace("✅ UPDATED {Type} folder: '{FolderName}'", 
                            item.GetType().Name, folderName);
                    }
                    else
                    {
                        addedItems.Add(item);
                        _logger.LogTrace("✅ CREATED {Type} folder: '{FolderName}'", 
                            item.GetType().Name, folderName);
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
                _logger.LogError(ex, "❌ ERROR processing item {Item} - MediaName: '{MediaName}', Id: {Id}", 
                    item, item.MediaName, item.Id);
            }
        }
        
        // Create all tasks and await them in parallel
        var tasks = items.Select((item, index) => ProcessCreateFolderMetadataAsync(item, index)).ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more tasks failed.");
        }
        
        _logger.LogDebug("Completed folder creation for mixed media - Added: {Added}, Updated: {Updated}", 
            addedItems.Count, updatedItems.Count);
        
        return (addedItems, updatedItems);
    }

    /// <summary>
    /// Clear the stored list rank of every title in the current library's folder that the current sync did not
    /// fetch, so the ranks in metadata.json are always the ranks of the latest sync. Without this, a title that
    /// dropped off the list keeps its old rank on disk and the next scheduled sort task (which trusts the stored
    /// ranks) puts it back among the fresh titles. .ignore'd and search-added titles are otherwise left alone —
    /// only the rank is removed. Call inside the library's LibraryScope.Enter.
    /// </summary>
    /// <param name="fetched">The items the current sync fetched.</param>
    /// <returns>The number of metadata files whose list rank was cleared.</returns>
    public async Task<int> ClearStaleListRanksAsync(IReadOnlyCollection<IJellyseerrItem> fetched)
    {
        var freshKeys = fetched.Select(item => (item.GetType(), item.Id)).ToHashSet();
        var (movies, shows) = await ReadMetadataAsync();
        var cleared = 0;

        foreach (IJellyseerrItem item in movies.Cast<IJellyseerrItem>().Concat(shows))
        {
            if (item.ListRank == null || freshKeys.Contains((item.GetType(), item.Id)))
            {
                continue;
            }

            try
            {
                var metadataFile = Path.Combine(GetJellyBridgeItemDirectory(item), IJellyseerrItem.GetMetadataFilename());
                if (!File.Exists(metadataFile))
                {
                    continue;
                }
                item.ListRank = null;
                await File.WriteAllTextAsync(metadataFile, JellyBridgeJsonSerializer.Serialize((object)item));
                cleared++;
                _logger.LogDebug("Cleared the stored list rank of '{MediaName}' (Id: {Id}) — it is no longer on the list", item.MediaName, item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clear the stale list rank of '{MediaName}'", item.MediaName);
            }
        }

        return cleared;
    }

    /// <summary>
    /// 8b fix 4: refresh the CreatedDate in the metadata.json of titles the fetch found ON the list but judged
    /// HIDDEN (a .ignore in their folder, or a match in a real library). They are never written, so without this
    /// their age keeps running and CleanupService.DeleteExpiredMedia removes them after the retention days even
    /// though the list still holds them. Only existing folders are touched — a hidden title is never created
    /// here, and nothing but the CreatedDate changes. Call inside the library's LibraryScope.Enter.
    /// </summary>
    /// <param name="hidden">The titles the current fetch judged hidden.</param>
    /// <returns>The number of metadata files whose CreatedDate was refreshed.</returns>
    public async Task<int> TouchHiddenCreatedDatesAsync(IEnumerable<IJellyseerrItem> hidden)
    {
        var touched = 0;
        var seen = new HashSet<(Type, int)>();
        foreach (var item in hidden)
        {
            if (!seen.Add((item.GetType(), item.Id)))
            {
                continue;
            }

            try
            {
                var metadataFile = Path.Combine(GetJellyBridgeItemDirectory(item), IJellyseerrItem.GetMetadataFilename());
                if (!File.Exists(metadataFile))
                {
                    continue; // a hidden title is never (re)created — only an existing folder gets its age refreshed
                }

                // Rewrite the file on disk with only its CreatedDate refreshed (same read/rewrite pattern as
                // ClearStaleListRanksAsync), so nothing else about the stored record changes.
                IJellyseerrItem? stored = item is JellyseerrMovie
                    ? JellyBridgeJsonSerializer.Deserialize<JellyseerrMovie>(await File.ReadAllTextAsync(metadataFile))
                    : JellyBridgeJsonSerializer.Deserialize<JellyseerrShow>(await File.ReadAllTextAsync(metadataFile));
                if (stored == null)
                {
                    continue;
                }

                stored.CreatedDate = DateTimeOffset.Now;
                await File.WriteAllTextAsync(metadataFile, JellyBridgeJsonSerializer.Serialize((object)stored));
                touched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh the created date of hidden '{MediaName}'", item.MediaName);
            }
        }

        return touched;
    }

    /// <summary>
    /// Write metadata for a single item to the appropriate folder.
    /// </summary>
    private async Task<bool> WriteMetadataAsync(IJellyseerrItem item)
    {
        try
        {
            var targetDirectory = GetJellyBridgeItemDirectory(item);

            // Create directory if it doesn't exist
            if (!FolderUtils.FolderExistsThrowNull(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                _logger.LogTrace("Created directory: {TargetDirectory}", targetDirectory);
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

    public async Task<(List<string> movieFolders, List<string> showFolders, List<string> mixedFolders)> CreateEmptyLibraryFoldersAsync()
    {
        var folders = new Dictionary<string, List<string>>();
        folders[JellyseerrMovie.LibraryType] = new List<string>();
        folders[JellyseerrShow.LibraryType] = new List<string>();
        folders[string.Empty] = new List<string>(); // For mixed media
        
        var baseDirectory = FolderUtils.GetBaseDirectory();
        var useMixedMediaLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseMixedMediaLibrary));

        if (!useMixedMediaLibrary)
        {
            foreach(var libraryType in new[] { JellyseerrMovie.LibraryType, JellyseerrShow.LibraryType })
            {
                string mediaFolderPath = Path.Combine(baseDirectory, libraryType);
                if (FolderUtils.FolderExistsThrowNull(mediaFolderPath))
                {
                    _logger.LogTrace("Media type folder already exists: {MediaFolder}", mediaFolderPath);
                }
                else
                {
                    Directory.CreateDirectory(mediaFolderPath);
                    _logger.LogDebug("Created media type folder: {MediaFolder}", mediaFolderPath);
                }
                folders[libraryType].Add(mediaFolderPath);
            }
        }
        else
        {
            if (FolderUtils.FolderExistsThrowNull(baseDirectory))
            {
                _logger.LogTrace("Media type folder already exists: {BaseDirectory}", baseDirectory);
            }
            else
            {
                Directory.CreateDirectory(baseDirectory);
                _logger.LogDebug("Created media type folder: {BaseDirectory}", baseDirectory);
            }
            folders[string.Empty].Add(baseDirectory);
        }
        
        await Task.CompletedTask; // Satisfy async requirement
        
        return (movieFolders: folders[JellyseerrMovie.LibraryType],
                showFolders: folders[JellyseerrShow.LibraryType],
                mixedFolders: folders[string.Empty]);
    }

    #region Helpers

    /// <summary>
    /// Get the directory path for a specific item.
    /// </summary>
    public string GetJellyBridgeItemDirectory(IJellyseerrItem? item = null)
    {
        var baseDirectory = FolderUtils.GetBaseDirectory();
        var combinedPath = baseDirectory;
        var useMixedMediaLibrary = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseMixedMediaLibrary));

        if (item == null)
        {
            return combinedPath;
        }
        var itemString = item.ToString();
        if (string.IsNullOrEmpty(itemString))
        {
            throw new ArgumentException($"Item {item.GetType().Name} returned null or empty string from ToString()", nameof(item));
        }
        var itemFolder = FolderUtils.SanitizeFileName(itemString);

        if (!useMixedMediaLibrary)
        {
            string? mediaFolder = null;
            if (item is JellyseerrMovie)
            {
                mediaFolder = JellyseerrMovie.LibraryType;
            }
            else if (item is JellyseerrShow)
            {
                mediaFolder = JellyseerrShow.LibraryType;
            }
            if (mediaFolder != null)
            {
                baseDirectory = Path.Combine(baseDirectory, mediaFolder);
            }
        }

        // Store in the library's folder with the folder name (Legacy separate mode: under Movies/Shows above)
        combinedPath = Path.Combine(baseDirectory, itemFolder);
        
        return combinedPath;
    }

    #endregion
}