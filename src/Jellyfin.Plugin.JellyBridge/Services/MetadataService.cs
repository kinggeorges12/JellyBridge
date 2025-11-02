using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
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

        try
        {
            // Get categorized directories
            var (movieDirectories, showDirectories) = ReadMetadataFolders();

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
    /// Discovers and categorizes directories containing metadata files.
    /// </summary>
    /// <returns>Tuple containing lists of movie directories and show directories</returns>
    public (List<string> movieDirectories, List<string> showDirectories) ReadMetadataFolders()
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
    public async Task<(List<IJellyseerrItem> added, List<IJellyseerrItem> updated)> CreateFolderMetadataAsync(List<IJellyseerrItem> items)
    {
        var addedItems = new List<IJellyseerrItem>();
        var updatedItems = new List<IJellyseerrItem>();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
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
                var folderName = GetJellyseerrItemDirectory(item);
                var folderExists = Directory.Exists(folderName);

                _logger.LogTrace("Folder details - Name: '{FolderName}', Exists: {FolderExists}", 
                    folderName, folderExists);

                // Write metadata
                var success = await WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderExists)
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
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("Completed folder creation for mixed media - Added: {Added}, Updated: {Updated}", 
            addedItems.Count, updatedItems.Count);
        
        return (addedItems, updatedItems);
    }

    /// <summary>
    /// Write metadata for a single item to the appropriate folder.
    /// </summary>
    private async Task<bool> WriteMetadataAsync(IJellyseerrItem item)
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

    #endregion
}