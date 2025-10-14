using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing bridge folders and metadata.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly ILogger<JellyseerrBridgeService> _logger;
    private readonly ILibraryManager _libraryManager;

    public JellyseerrBridgeService(ILogger<JellyseerrBridgeService> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Get all existing items of a specific type from Jellyfin libraries.
    /// </summary>
    public Task<List<T>> GetExistingItemsAsync<T>() where T : BaseItem
    {
        var items = new List<T>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogInformation("[JellyseerrBridge] Sync directory: {SyncDirectory}", syncDirectory);

        try
        {
            // Get all libraries
            var libraries = _libraryManager.GetVirtualFolders();
            
            foreach (var library in libraries)
            {
                _logger.LogInformation("[JellyseerrBridge] Processing library: {LibraryName} (Type: {LibraryType})", 
                    library.Name, library.CollectionType);

                // Get items from this library
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { typeof(T).Name == "Movie" ? BaseItemKind.Movie : BaseItemKind.Series },
                    Recursive = true
                };

                var libraryItems = _libraryManager.GetItemsResult(query).Items.Cast<T>().ToList();
                items.AddRange(libraryItems);
                
                _logger.LogInformation("[JellyseerrBridge] Found {ItemCount} {ItemType} in library {LibraryName}", 
                    libraryItems.Count, typeof(T).Name, library.Name);
            }

            _logger.LogInformation("[JellyseerrBridge] Total {ItemType} found across all libraries: {TotalCount}", 
                typeof(T).Name, items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error getting existing {ItemType} from libraries", typeof(T).Name);
        }

        return Task.FromResult(items);
    }

    /// <summary>
    /// Filter items to exclude those that already exist in main libraries.
    /// </summary>
    public async Task<List<TJellyseerr>> FilterItemsAsync<TJellyseerr, TExisting>(List<TJellyseerr> items, string itemTypeName) 
        where TJellyseerr : SearchResult, IJellyseerrMedia, IEquatable<TJellyseerr>
        where TExisting : BaseItem
    {
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;
        
        if (!excludeFromMainLibraries)
        {
            _logger.LogInformation("[JellyseerrBridge] ExcludeFromMainLibraries is disabled - returning all {ItemCount} {ItemType}", items.Count, itemTypeName);
            return items;
        }

        var existingItems = await GetExistingItemsAsync<TExisting>();
        _logger.LogInformation("[JellyseerrBridge] Found {ExistingItemCount} existing {ItemType} in main libraries", existingItems.Count, itemTypeName);

        var filteredItems = new List<TJellyseerr>();
        int skippedCount = 0;

        foreach (var item in items)
        {
            if (existingItems.Any(existing => item.Equals(existing)))
            {
                _logger.LogInformation("[JellyseerrBridge] Skipping {ItemType} {ItemName} - already exists in main libraries", itemTypeName, item.Name);
                skippedCount++;
                continue;
            }

            filteredItems.Add(item);
        }

        _logger.LogInformation("[JellyseerrBridge] Filtered {OriginalCount} {ItemType} to {FilteredCount} (skipped {SkippedCount})", 
            items.Count, itemTypeName, filteredItems.Count, skippedCount);

        return filteredItems;
    }

    /// <summary>
    /// Find matches between existing Jellyfin items and bridge metadata.
    /// </summary>
    public async Task<List<TJellyseerr>> FindMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> existingItems, 
        List<TJellyseerr> bridgeMetadata) 
        where TJellyfin : BaseItem 
        where TJellyseerr : SearchResult, IJellyseerrMedia, IEquatable<TJellyseerr>
    {
        var matches = new List<TJellyseerr>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        foreach (var existingItem in existingItems)
        {
            // Use the built-in IEquatable<TJellyfin> implementation
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => bm.Equals(existingItem));

            if (bridgeMatch != null)
            {
                // Find the bridge folder directory for this item
                var bridgeFolderPath = await FindBridgeFolderPathAsync(syncDirectory, bridgeMatch);
                
                // Create .ignore file with Jellyfin item metadata
                if (!string.IsNullOrEmpty(bridgeFolderPath))
                {
                    await CreateIgnoreFileAsync(bridgeFolderPath, existingItem);
                }

                // Add the actual bridge model to matches
                matches.Add(bridgeMatch);
            }
        }

        _logger.LogInformation("[JellyseerrBridge] Found {MatchCount} matches between Jellyfin items and bridge metadata", matches.Count);
        return matches;
    }

    /// <summary>
    /// Find the bridge folder path for a given item.
    /// </summary>
    public async Task<string?> FindBridgeFolderPathAsync<TJellyseerr>(string baseDirectory, TJellyseerr item) 
        where TJellyseerr : SearchResult, IJellyseerrMedia, IEquatable<TJellyseerr>
    {
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return null;
        }

        try
        {
            // Look for folders that might contain this item
            var directories = Directory.GetDirectories(baseDirectory, "*", SearchOption.AllDirectories);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        var metadata = JsonSerializer.Deserialize<TJellyseerr>(json);
                        
                        if (metadata != null && item.Equals(metadata))
                        {
                            return directory;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyseerrBridge] Error reading metadata file: {MetadataFile}", metadataFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error finding bridge folder path for item: {ItemName}", item.Name);
        }

        return null;
    }

    /// <summary>
    /// Create an ignore file for a Jellyfin item in the bridge folder.
    /// </summary>
    private async Task CreateIgnoreFileAsync(string bridgeFolderPath, BaseItem item)
    {
        try
        {
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            var itemJson = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(ignoreFilePath, itemJson);
            _logger.LogInformation("[JellyseerrBridge] Created ignore file for {ItemName} in {BridgeFolder}", item.Name, bridgeFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error creating ignore file for {ItemName}", item.Name);
        }
    }

    /// <summary>
    /// Read bridge folder metadata for a specific type.
    /// </summary>
    public async Task<List<T>> ReadBridgeFolderMetadataAsync<T>() where T : class
    {
        var metadata = new List<T>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
        {
            _logger.LogWarning("[JellyseerrBridge] Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
            return metadata;
        }

        try
        {
            var directories = Directory.GetDirectories(syncDirectory, "*", SearchOption.AllDirectories);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogDebug("[JellyseerrBridge] Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                        
                        var item = JsonSerializer.Deserialize<T>(json);
                        
                        if (item != null)
                        {
                            _logger.LogDebug("[JellyseerrBridge] Successfully deserialized item: {Item}", item.ToString());
                            metadata.Add(item);
                        }
                        else
                        {
                            _logger.LogWarning("[JellyseerrBridge] Failed to deserialize item from {MetadataFile}", metadataFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyseerrBridge] Error reading metadata file: {MetadataFile}", metadataFile);
                    }
                }
            }

            _logger.LogInformation("[JellyseerrBridge] Read {Count} {ItemType} metadata files from bridge folders", 
                metadata.Count, typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error reading bridge folder metadata for {ItemType}", typeof(T).Name);
        }

        return metadata;
    }

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// </summary>
    public async Task<List<IJellyseerrMedia>> TestLibraryScanAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;

        try
        {
            _logger.LogInformation("Testing Jellyfin library scan functionality against bridge folder metadata...");
            
            // For test purposes, always scan for bridge items regardless of ExcludeFromMainLibraries setting
            // This allows users to see what bridge items exist even when the setting is disabled
            
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return new List<IJellyseerrMedia>();
            }

            // Get existing Jellyfin items
            var existingMovies = await GetExistingItemsAsync<Movie>();
            var existingShows = await GetExistingItemsAsync<Series>();

            // Read bridge folder metadata
            var bridgeMovieMetadata = await ReadBridgeFolderMetadataAsync<JellyseerrMovie>();
            var bridgeShowMetadata = await ReadBridgeFolderMetadataAsync<JellyseerrShow>();

            // Compare and find matches
            var movieMatches = await FindMatches(existingMovies, bridgeMovieMetadata);
            var showMatches = await FindMatches(existingShows, bridgeShowMetadata);

            // Combine all bridge metadata into a single list
            var allBridgeItems = new List<IJellyseerrMedia>();
            allBridgeItems.AddRange(bridgeMovieMetadata.Cast<IJellyseerrMedia>());
            allBridgeItems.AddRange(bridgeShowMetadata.Cast<IJellyseerrMedia>());

            _logger.LogInformation("Library scan test completed. Found {MovieCount} movies, {ShowCount} shows, {MatchCount} matches", 
                bridgeMovieMetadata.Count, bridgeShowMetadata.Count, movieMatches.Count + showMatches.Count);

            return allBridgeItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return new List<IJellyseerrMedia>();
        }
    }
}