using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
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

    /// <summary>
    /// Static folder name template based on the type.
    /// </summary>
    private static readonly string FolderTemplate = typeof(TJellyseerr).Name switch
    {
        nameof(JellyseerrMovie) => "{MediaName} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]",
        nameof(JellyseerrShow) => "{MediaName} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]",
        _ => "{MediaName} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]"
    };

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
    /// Create folder name using the static template for this type.
    /// </summary>
    public string CreateFolderName(TJellyseerr item)
    {
        var folderName = FolderTemplate;
        _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Starting with template '{Template}' for item type '{ItemType}'", 
            FolderTemplate, typeof(TJellyseerr).Name);
        
        _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Item details - MediaName: '{MediaName}', Year: '{Year}', Id: {Id}, ExtraId: '{ExtraId}', ExtraIdName: '{ExtraIdName}'", 
            item.MediaName, item.Year, item.Id, item.ExtraId, item.ExtraIdName);
        
        // Find all fields in the template
        var fieldPattern = @"\{([^}]+)\}";
        var matches = Regex.Matches(FolderTemplate, fieldPattern);
        
        foreach (Match match in matches)
        {
            var field = match.Groups[0].Value; // Full field like "{title}"
            var propertyPath = match.Groups[1].Value; // Property path like "title" or "mediaInfo.tvdbId"
            
            _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Processing field '{Field}' -> property '{PropertyPath}'", 
                field, propertyPath);
            
            var value = GetPropertyValue(item, propertyPath);
            folderName = folderName.Replace(field, value);
            
            _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Replaced '{Field}' with '{Value}', result: '{FolderName}'", 
                field, value, folderName);
        }
        
        // Remove empty field blocks like [tvdbid-], [tmdbid-], [imdbid-] and empty parentheses ()
        // Be more careful with spacing - don't remove spaces between title and ID when year is missing
        folderName = Regex.Replace(folderName, @"\s*(\[[^]]*-\])\s*", " "); // Remove empty ID brackets but keep space
        folderName = Regex.Replace(folderName, @"\(\s*\)", ""); // Remove empty parentheses
        folderName = Regex.Replace(folderName, @"\s+", " "); // Normalize multiple spaces to single space
        folderName = folderName.Trim(); // Remove leading/trailing spaces
        
        var finalResult = SanitizeFileName(folderName);
        _logger.LogDebug("[JellyseerrFolderManager] CreateFolderName: Final result: '{FinalResult}'", finalResult);
        return finalResult;
    }

    /// <summary>
    /// Get property value using reflection.
    /// </summary>
    private string GetPropertyValue(IJellyseerrItem item, string propertyPath)
    {
        if (item == null || string.IsNullOrEmpty(propertyPath))
        {
            _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: item is null or propertyPath is empty");
            return "";
        }
        
        _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: Starting to get property '{PropertyPath}' from item type '{ItemType}'", 
            propertyPath, item.GetType().Name);
        
        var currentObject = (object)item;
        var pathParts = propertyPath.Split('.');
        
        foreach (var part in pathParts)
        {
            if (currentObject == null)
            {
                _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: currentObject is null for part '{Part}'", part);
                return "";
            }
            
            var type = currentObject.GetType();
            var property = type.GetProperty(part);
            
            if (property == null)
            {
                _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: Property '{Part}' not found on type '{Type}'", part, type.Name);
                // List all available properties for debugging
                var availableProperties = type.GetProperties().Select(p => p.Name).ToArray();
                _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: Available properties on '{Type}': [{AvailableProperties}]", 
                    type.Name, string.Join(", ", availableProperties));
                return "";
            }
            
            try
            {
                currentObject = property.GetValue(currentObject);
                _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: '{Part}' = '{Value}' (Type: {ValueType})", 
                    part, currentObject, currentObject?.GetType().Name ?? "null");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyseerrFolderManager] GetPropertyValue: Error getting property '{Part}'", part);
                return "";
            }
        }
        
        if (currentObject == null)
        {
            _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: Final value is null for '{PropertyPath}'", propertyPath);
            return "";
        }
        
        // Convert to string
        var result = currentObject.ToString() ?? "";
        _logger.LogDebug("[JellyseerrFolderManager] GetPropertyValue: Final result for '{PropertyPath}' = '{Result}'", propertyPath, result);
        return result;
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
                        
                        var jsonOptions = new JsonSerializerOptions 
                        { 
                            WriteIndented = true,
                            PropertyNamingPolicy = null, // Use JsonPropertyName attributes instead of naming policy
                            PropertyNameCaseInsensitive = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault // ignores empty base properties
                        };
                        var item = JsonSerializer.Deserialize<TJellyseerr>(json, jsonOptions);
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

            // Serialize and write metadata file
            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = null, // Use JsonPropertyName attributes instead of naming policy
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault // ignores empty base properties
            };
            
            var json = JsonSerializer.Serialize(item, jsonOptions);
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