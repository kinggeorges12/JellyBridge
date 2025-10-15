using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Serialization;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Linq;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Static utility class for folder management operations.
/// </summary>
public static class JellyseerrFolderUtils
{
    /// <summary>
    /// Static base directory path from settings.
    /// </summary>
    private static string? _staticBaseDirectory;

    /// <summary>
    /// Get the base directory from settings or return a default.
    /// </summary>
    /// <returns>The configured base directory path</returns>
    public static string GetBaseDirectory()
    {
        if (_staticBaseDirectory == null)
        {
            _staticBaseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        }
        return _staticBaseDirectory;
    }


    /// <summary>
    /// Check if a given path is within the sync directory base path.
    /// </summary>
    /// <param name="pathToCheck">The path to check</param>
    /// <param name="syncDirectory">The sync directory base path (optional, uses static base directory if null)</param>
    /// <returns>True if the path is within the sync directory</returns>
    public static bool IsPathInSyncDirectory(string? pathToCheck, string? syncDirectory = null)
    {
        if (string.IsNullOrEmpty(pathToCheck))
            return false;

        var directoryToCheck = syncDirectory ?? GetBaseDirectory();

        try
        {
            var normalizedPath = Path.GetFullPath(pathToCheck);
            var normalizedSyncPath = Path.GetFullPath(directoryToCheck);
            
            return normalizedPath.StartsWith(normalizedSyncPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // If path normalization fails, fall back to string comparison
            return pathToCheck.StartsWith(directoryToCheck, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// Generic folder manager for reading and writing Jellyseerr metadata files.
/// </summary>
/// <typeparam name="TJellyseerr">The type of Jellyseerr item (JellyseerrMovie or JellyseerrShow)</typeparam>
public class JellyseerrFolderManager<TJellyseerr> where TJellyseerr : class, IJellyseerrItem
{
    private readonly ILogger _logger;
    private readonly string _baseDirectory;

    public JellyseerrFolderManager(ILogger logger, string baseDirectory)
    {
        _logger = logger;
        _baseDirectory = baseDirectory;
    }

    /// <summary>
    /// Get the library type for this generic type using reflection.
    /// </summary>
    private string GetLibraryType()
    {
        var property = typeof(TJellyseerr).GetProperty("LibraryType", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        
        if (property != null)
        {
            var value = property.GetValue(null);
            return value as string ?? "Unknown";
        }
        
        return "Unknown";
    }

    /// <summary>
    /// Create folder name using the ToString() method from IJellyseerrItem.
    /// </summary>
    public string CreateFolderName(TJellyseerr item)
    {
        _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Using ToString() for item type '{ItemType}'", 
            typeof(TJellyseerr).Name);
        
        _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Item details - MediaName: '{MediaName}', Year: '{Year}', Id: {Id}", 
            item.MediaName, item.Year, item.Id);
        
        var folderName = item.ToString();
        
        if (string.IsNullOrWhiteSpace(folderName))
        {
            throw new InvalidOperationException($"Cannot create folder name for {typeof(TJellyseerr).Name}: ToString() returned null or empty value");
        }
        
        var finalResult = SanitizeFileName(folderName);
        _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Final result: '{FinalResult}'", finalResult);
        return finalResult;
    }

    /// <summary>
    /// Sanitize filename by removing invalid characters.
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    /// <summary>
    /// Read all metadata files for this type from the appropriate folder.
    /// </summary>
    public async Task<List<TJellyseerr>> ReadMetadataAsync()
    {
        var items = new List<TJellyseerr>();
        var typeFolder = GetLibraryType();
        var typeDirectory = Path.Combine(_baseDirectory, typeFolder);

        if (!Directory.Exists(typeDirectory))
        {
            _logger.LogDebug("[JellyseerrFolderManager] Type directory does not exist: {TypeDirectory}", typeDirectory);
            return items;
        }

        try
        {
            var directories = Directory.GetDirectories(typeDirectory, "*", SearchOption.TopDirectoryOnly);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogDebug("[JellyseerrFolderManager] Reading {Type} metadata from {MetadataFile}: {Json}", typeFolder, metadataFile, json);
                        
                        var item = JellyseerrJsonSerializer.Deserialize<TJellyseerr>(json);
                        if (item != null)
                        {
                            _logger.LogInformation("[JellyseerrFolderManager] Successfully deserialized {Type} - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                typeFolder, item.MediaName, item.Id, item.MediaType, item.Year);
                            items.Add(item);
                        }
                        else
                        {
                            _logger.LogWarning("[JellyseerrFolderManager] Failed to deserialize {Type} from {MetadataFile}", typeFolder, metadataFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyseerrFolderManager] Error reading {Type} metadata file: {MetadataFile}", typeFolder, metadataFile);
                    }
                }
            }

            _logger.LogInformation("[JellyseerrFolderManager] Read {Count} {Type} items from {TypeDirectory}", 
                items.Count, typeFolder, typeDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrFolderManager] Error reading {Type} metadata from {TypeDirectory}", typeFolder, typeDirectory);
        }

        return items;
    }

    /// <summary>
    /// Write metadata for a single item to the appropriate folder.
    /// </summary>
    public async Task<bool> WriteMetadataAsync(TJellyseerr item)
    {
        try
        {
            var typeFolder = GetLibraryType();
            var folderName = CreateFolderName(item);
            var targetDirectory = Path.Combine(_baseDirectory, typeFolder, folderName);

            // Create directory if it doesn't exist
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                _logger.LogDebug("[JellyseerrFolderManager] Created {Type} directory: {TargetDirectory}", typeFolder, targetDirectory);
            }

            var json = JellyseerrJsonSerializer.Serialize(item);
            var metadataFile = Path.Combine(targetDirectory, "metadata.json");
            
            await File.WriteAllTextAsync(metadataFile, json);
            _logger.LogDebug("[JellyseerrFolderManager] Wrote {Type} metadata to {MetadataFile}", typeFolder, metadataFile);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrFolderManager] Error writing {Type} metadata for {ItemMediaName}", GetLibraryType(), item.MediaName);
            return false;
        }
    }

    /// <summary>
    /// Get the directory path for a specific item.
    /// </summary>
    public string GetItemDirectory(TJellyseerr item)
    {
        var typeFolder = GetLibraryType();
        var folderName = CreateFolderName(item);
        return Path.Combine(_baseDirectory, typeFolder, folderName);
    }
}