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
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                throw new InvalidOperationException($"Folder path does not exist: {folderPath}");
            }

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
    /// Write metadata for a single item to the appropriate folder.
    /// </summary>
    private async Task<bool> WriteMetadataAsync(IJellyseerrItem item)
    {
        try
        {
            var targetDirectory = GetJellyBridgeItemDirectory(item);

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


    /// <summary>
    /// Create empty network folders based on the network configuration.
    /// </summary>
    /// <returns>Tuple containing lists of created and existing folder names</returns>
    public async Task<(List<string> createdFolders, List<string> existingFolders)> CreateEmptyNetworkFoldersAsync()
    {
        var createdFolders = new List<string>();
        var existingFolders = new List<string>();
        
        var config = Plugin.GetConfiguration();
        var baseDirectory = FolderUtils.GetBaseDirectory();
        var networkMap = config.NetworkMap ?? new List<JellyseerrNetwork>();
        
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            throw new InvalidOperationException($"Library Directory does not exist: {baseDirectory}");
        }
        
        if (networkMap.Count == 0)
        {
            _logger.LogWarning("No networks configured. No folders will be created.");
            return (createdFolders, existingFolders);
        }
        
        var useNetworkFolders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));
        if (!useNetworkFolders)
        {
            _logger.LogWarning("UseNetworkFolders is not enabled. No folders will be created.");
            return (createdFolders, existingFolders);
        }
        
        _logger.LogInformation("Starting network folder generation - Base Directory: {BaseDirectory}, Network Count: {NetworkCount}", 
            baseDirectory, networkMap.Count);
        
        try
        {
            foreach (var network in networkMap)
            {
                try
                {
                    if (string.IsNullOrEmpty(network.Name))
                    {
                        _logger.LogWarning("Skipping network with empty name: {NetworkId}", network.Id);
                        continue;
                    }
                    
                    // Use GetNetworkFolder which handles the prefix
                    var networkFolderPath = GetNetworkFolder(network.Name);
                    if (networkFolderPath == null)
                    {
                        _logger.LogWarning("GetNetworkFolder returned null for network: {NetworkName}", network.Name);
                        continue;
                    }
                    
                    var networkFolderName = Path.GetFileName(networkFolderPath);
                    
                    if (Directory.Exists(networkFolderPath))
                    {
                        existingFolders.Add(networkFolderName);
                        _logger.LogTrace("Network folder already exists: {NetworkFolder}", networkFolderPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(networkFolderPath);
                        createdFolders.Add(networkFolderName);
                        _logger.LogInformation("Created network folder: {NetworkFolder}", networkFolderPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating folder for network: {NetworkName}", network.Name);
                    throw; // Re-throw to fail the entire operation
                }
            }
            
            _logger.LogInformation("Network folder generation completed - Created: {Created}, Existing: {Existing}", 
                createdFolders.Count, existingFolders.Count);
            
            await Task.CompletedTask; // Satisfy async requirement
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating network folders");
            throw;
        }
        
        return (createdFolders, existingFolders);
    }

    #region Helpers

    /// <summary>
    /// Get the network folder path if UseNetworkFolders is enabled, otherwise returns null.
    /// </summary>
    /// <param name="networkName">The name of the network (from NetworkTag or network.Name)</param>
    /// <returns>Network folder path if enabled, null otherwise</returns>
    public string? GetNetworkFolder(string? networkName)
    {
        if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders)) || string.IsNullOrEmpty(networkName))
        {
            return null;
        }
        
        var networkPrefix = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryPrefix));
        var networkFolderName = FolderUtils.SanitizeFileName(networkPrefix + networkName);
        var baseDirectory = FolderUtils.GetBaseDirectory();
        return Path.Combine(baseDirectory, networkFolderName);
    }

    /// <summary>
    /// Get the directory path for a specific item.
    /// </summary>
    public string GetJellyBridgeItemDirectory(IJellyseerrItem? item = null)
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
        var networkFolder = GetNetworkFolder(item.NetworkTag);
        if (networkFolder != null)
        {
            return Path.Combine(networkFolder, itemFolder);
        }
        // If not using network prefix, just store in the base directory with the folder name
        return Path.Combine(FolderUtils.GetBaseDirectory(), itemFolder);
    }

    #endregion
}