using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing metadata files and folder operations for Jellyseerr bridge items.
/// </summary>
public class MetadataService
{
    private readonly DebugLogger<MetadataService> _logger;

    public MetadataService(ILogger<MetadataService> logger)
    {
        _logger = new DebugLogger<MetadataService>(logger);
    }

    /// <summary>
    /// Read all metadata files from the bridge folder, detecting movie vs show based on NFO files.
    /// </summary>
    public async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadMetadataAsync()
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

            // Get all subdirectories that contain metadata files
            var metadataFiles = Directory.GetFiles(syncDirectory, IJellyseerrItem.GetMetadataFilename(), SearchOption.AllDirectories);
            
            foreach (var metadataFile in metadataFiles)
            {
                var directory = Path.GetDirectoryName(metadataFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogDebug("Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                        
                        // Check for movie.nfo to identify movie folders
                        var movieNfoFile = Path.Combine(directory, JellyseerrMovie.GetNfoFilename());
                        var showNfoFile = Path.Combine(directory, JellyseerrShow.GetNfoFilename());
                        
                        if (File.Exists(movieNfoFile))
                        {
                            // This is a movie folder
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
                        else if (File.Exists(showNfoFile))
                        {
                            // This is a show folder
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
                        else
                        {
                            _logger.LogWarning("No NFO file found in directory {Directory} - skipping", directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error reading metadata file: {MetadataFile}", metadataFile);
                    }
                }
            }

            _logger.LogDebug("Read {MovieCount} movies and {ShowCount} shows from bridge folders", 
                movies.Count, shows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading metadata from {SyncDirectory}", syncDirectory);
        }

        return (movies, shows);
    }

    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows using JellyseerrFolderManager.
    /// </summary>
    public async Task<(List<TJellyseerr> added, List<TJellyseerr> updated)> CreateFoldersAsync<TJellyseerr>(List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var addedItems = new List<TJellyseerr>();
        var updatedItems = new List<TJellyseerr>();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogDebug("CreateFoldersAsync: Starting folder creation for {ItemType} - Base Directory: {BaseDirectory}, Items Count: {ItemCount}", 
            typeof(TJellyseerr).Name, baseDirectory, items.Count);
        
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                _logger.LogTrace("CreateFoldersAsync: Processing item {ItemNumber}/{TotalItems} - MediaName: '{MediaName}', Id: {Id}, Year: '{Year}'", 
                    i + 1, items.Count, item.MediaName, item.Id, item.Year);
                
                // Generate folder name and get directory path
                var folderName = GetJellyseerrItemDirectory(item);
                var folderExists = Directory.Exists(folderName);

                _logger.LogTrace("CreateFoldersAsync: Folder details - Name: '{FolderName}', Exists: {FolderExists}", 
                    folderName, folderExists);

                // Write metadata using folder manager
                var success = await WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderExists)
                    {
                        updatedItems.Add(item);
                        _logger.LogTrace("CreateFoldersAsync: ✅ UPDATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                    else
                    {
                        addedItems.Add(item);
                        _logger.LogTrace("CreateFoldersAsync: ✅ CREATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                }
                else
                {
                    _logger.LogError("CreateFoldersAsync: ❌ FAILED to create folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                        item, item.MediaName, item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateFoldersAsync: ❌ ERROR creating folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                    item, item.MediaName, item.Id);
            }
        }
        
        _logger.LogDebug("CreateFoldersAsync: Completed folder creation for {ItemType} - Added: {Added}, Updated: {Updated}", 
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
            _logger.LogDebug("Wrote metadata to {MetadataFile}", metadataFile);
            
            // Write XML metadata only if NFO file doesn't exist
            var xmlFile = Path.Combine(targetDirectory, IJellyseerrItem.GetNfoFilename(item));
            if (!File.Exists(xmlFile))
            {
                var xmlText = item.ToXmlString();
                await File.WriteAllTextAsync(xmlFile, xmlText);
                _logger.LogDebug("Wrote XML to {XmlFile}", xmlFile);
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

    #endregion
}