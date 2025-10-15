using System.Text.Json;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

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
        nameof(JellyseerrMovie) => "{Name} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]",
        nameof(JellyseerrShow) => "{Name} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]",
        _ => "{Name} ({Year}) [tmdbid-{Id}] [{ExtraIdName}-{ExtraId}]"
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
        
        // Find all fields in the template
        var fieldPattern = @"\{([^}]+)\}";
        var matches = Regex.Matches(FolderTemplate, fieldPattern);
        
        foreach (Match match in matches)
        {
            var field = match.Groups[0].Value; // Full field like "{title}"
            var propertyPath = match.Groups[1].Value; // Property path like "title" or "mediaInfo.tvdbId"
            
            var value = GetPropertyValue(item, propertyPath);
            folderName = folderName.Replace(field, value);
        }
        
        // Remove empty field blocks like [tvdbid-], [tmdbid-], [imdbid-] and empty parentheses ()
        // Be more careful with spacing - don't remove spaces between title and ID when year is missing
        folderName = Regex.Replace(folderName, @"\s*(\[[^]]*-\])\s*", " "); // Remove empty ID brackets but keep space
        folderName = Regex.Replace(folderName, @"\(\s*\)", ""); // Remove empty parentheses
        folderName = Regex.Replace(folderName, @"\s+", " "); // Normalize multiple spaces to single space
        folderName = folderName.Trim(); // Remove leading/trailing spaces
        
        return SanitizeFileName(folderName);
    }

    /// <summary>
    /// Get property value using reflection.
    /// </summary>
    private string GetPropertyValue(IJellyseerrItem item, string propertyPath)
    {
        if (item == null || string.IsNullOrEmpty(propertyPath))
            return "";
        
        var currentObject = (object)item;
        var pathParts = propertyPath.Split('.');
        
        foreach (var part in pathParts)
        {
            if (currentObject == null)
                return "";
            
            var type = currentObject.GetType();
            var property = type.GetProperty(part);
            
            if (property == null)
                return "";
            
            currentObject = property.GetValue(currentObject);
        }
        
        if (currentObject == null)
            return "";
        
        // Convert to string
        return currentObject.ToString() ?? "";
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
                        _logger.LogDebug("[JellyseerrFolderManager] Reading {Type} metadata from {MetadataFile}", typeFolder, metadataFile);
                        
                        var item = JsonSerializer.Deserialize<TJellyseerr>(json);
                        if (item != null)
                        {
                            _logger.LogInformation("[JellyseerrFolderManager] Successfully deserialized {Type} - Name: '{Name}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                typeFolder, item.Name, item.Id, item.MediaType, item.Year);
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
            var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
            var metadataFile = Path.Combine(targetDirectory, "metadata.json");
            
            await File.WriteAllTextAsync(metadataFile, json);
            _logger.LogDebug("[JellyseerrFolderManager] Wrote {Type} metadata to {MetadataFile}", typeFolder, metadataFile);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrFolderManager] Error writing {Type} metadata for {ItemName}", GetLibraryType(), item.Name);
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