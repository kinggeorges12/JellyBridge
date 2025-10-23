using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Utils;
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
    /// Get the base directory from settings or return a default.
    /// </summary>
    /// <returns>The configured base directory path</returns>
    public static string GetBaseDirectory()
    {
        return Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
    }


    /// <summary>
    /// Check if a given path is within the sync directory base path.
    /// </summary>
    /// <param name="pathToCheck">The path to check</param>
    /// <param name="syncDirectory">The sync directory base path (optional, uses static base directory if null)</param>
    /// <returns>True if the path is within the sync directory</returns>
    public static bool IsPathInSyncDirectory(string? pathToCheck)
    {
        if (string.IsNullOrEmpty(pathToCheck))
            return false;

        var syncDirectory = GetBaseDirectory();

        try
        {
            var normalizedPath = Path.GetFullPath(pathToCheck);
            var normalizedSyncPath = Path.GetFullPath(syncDirectory);
            
            var result = normalizedPath.StartsWith(normalizedSyncPath, StringComparison.OrdinalIgnoreCase);
            
            return result;
        }
        catch (Exception)
        {
            // If path normalization fails, fall back to string comparison
            return pathToCheck.StartsWith(syncDirectory, StringComparison.OrdinalIgnoreCase);
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

    public JellyseerrFolderManager(string? baseDirectory = null)
    {
        var loggerFactory = Plugin.Instance?.LoggerFactory;
        _logger = loggerFactory?.CreateLogger<JellyseerrFolderManager<TJellyseerr>>() ?? throw new InvalidOperationException("Cannot create logger");
        _baseDirectory = baseDirectory ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
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

        // Normalize to reduce compatibility forms (e.g., fullwidth characters)
        var normalized = fileName.Normalize(NormalizationForm.FormKC);

        // Map common lookalikes to ASCII equivalents (colon, slashes, quotes, etc.)
        var charReplacements = new Dictionary<char, char>
        {
            { '：', ':' }, { '﹕', ':' }, { '꞉', ':' },
            { '／', '/' }, { '∕', '/' },
            { '＼', '\\' },
            { '？', '?' }, { '＊', '*' },
            { '＂', '"' },
            { '’', '\'' }, { '‘', '\'' }, { 'ʼ', '\'' },
            { '＜', '<' }, { '＞', '>' },
            { '｜', '|' }
        };

        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (charReplacements.TryGetValue(ch, out var mapped))
            {
                sb.Append(mapped);
            }
            else
            {
                sb.Append(ch);
            }
        }

        var mappedString = sb.ToString();

        // Apply string-level replacements in a centralized loop
        var stringReplacements = new (string from, string to)[]
        {
            ("\\u0027", "'"),
            ("&#39;", "'"),
            ("&apos;", "'"),
            (":", " -") // Replace colons with conventional separator using existing trailing space
        };
        foreach (var (from, to) in stringReplacements)
        {
            mappedString = mappedString.Replace(from, to);
        }

        // Remove invalid file name characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var withoutInvalids = string.Join("_", mappedString.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Replace only unsafe unicode categories; allow all other symbols (#, +, !, etc.)
        sb = new StringBuilder(withoutInvalids.Length);
        foreach (var ch in withoutInvalids)
        {
            var category = char.GetUnicodeCategory(ch);
            var isSafeCategory = category != System.Globalization.UnicodeCategory.Control && category != System.Globalization.UnicodeCategory.PrivateUse;
            sb.Append(isSafeCategory ? ch : '_');
        }

        var cleaned = sb.ToString();

        // Normalize whitespace
        cleaned = Regex.Replace(cleaned, "\\s{2,}", " ");             // collapse multiple spaces
        cleaned = Regex.Replace(cleaned, "_+", "_");                  // collapse multiple underscores
        cleaned = cleaned.Trim().Trim('-', '_', '.');

        return cleaned;
    }

    /// <summary>
    /// Read all metadata files for this type from the appropriate folder.
    /// </summary>
    public async Task<List<TJellyseerr>> ReadMetadataAsync()
    {
        var items = new List<TJellyseerr>();
        var typeDirectory = GetItemDirectory();

        try
        {
            var directories = Directory.GetDirectories(typeDirectory, "*", SearchOption.TopDirectoryOnly);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (System.IO.File.Exists(metadataFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        _logger.LogDebug("[JellyseerrFolderManager] Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                        
                        var item = JellyseerrJsonSerializer.Deserialize<TJellyseerr>(json);
                        if (item != null)
                        {
                            _logger.LogInformation("[JellyseerrFolderManager] Successfully deserialized - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                                item.MediaName, item.Id, item.MediaType, item.Year);
                            items.Add(item);
                        }
                        else
                        {
                            _logger.LogWarning("[JellyseerrFolderManager] Failed to deserialize from {MetadataFile}", metadataFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyseerrFolderManager] Error reading metadata file: {MetadataFile}", metadataFile);
                    }
                }
            }

            _logger.LogInformation("[JellyseerrFolderManager] Read {Count} items from {TypeDirectory}", 
                items.Count, typeDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrFolderManager] Error reading metadata from {TypeDirectory}", typeDirectory);
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
            var targetDirectory = GetItemDirectory(item);

            // Create directory if it doesn't exist
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                _logger.LogDebug("[JellyseerrFolderManager] Created directory: {TargetDirectory}", targetDirectory);
            }

            var json = JellyseerrJsonSerializer.Serialize(item);
            var metadataFile = Path.Combine(targetDirectory, "metadata.json");
            
            await File.WriteAllTextAsync(metadataFile, json);
            _logger.LogDebug("[JellyseerrFolderManager] Wrote metadata to {MetadataFile}", metadataFile);
            
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
    public string GetItemDirectory(TJellyseerr? item = null)
    {
        var typeFolder = GetLibraryType();
        if (item == null)
        {
            return Path.Combine(_baseDirectory, typeFolder);
        }
        var folderName = CreateFolderName(item);
        return Path.Combine(_baseDirectory, typeFolder, folderName);
    }
}