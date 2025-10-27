using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyBridge.Configuration;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// Static utility class for folder management operations.
/// </summary>
public static class FolderUtils
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

    /// <summary>
    /// Sanitize filename by removing invalid characters.
    /// </summary>
    public static string SanitizeFileName(string fileName)
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
            { '\u2019', '\'' }, { '\u2018', '\'' }, { '\u02BC', '\'' },
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

        // Remove invalid file name characters and replace with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        // Also filter out invisible/private use characters that might cause display issues
        var replaced = new StringBuilder(mappedString.Length);
        foreach (var ch in mappedString)
        {
            var category = char.GetUnicodeCategory(ch);
            // Skip invisible characters and private use characters
            if (category == System.Globalization.UnicodeCategory.PrivateUse || 
                category == System.Globalization.UnicodeCategory.Control ||
                (ch >= 0x200B && ch <= 0x200D) || // Zero-width space, zero-width non-joiner, zero-width joiner
                (ch >= 0xFEFF && ch <= 0xFEFF))  // Zero-width no-break space
            {
                // Skip these invisible characters
                continue;
            }
            replaced.Append(invalidChars.Contains(ch) ? '_' : ch);
        }
        var withoutInvalids = replaced.ToString();

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
        cleaned = cleaned.TrimStart();  // Trim leading whitespace only
        cleaned = cleaned.TrimEnd(' ', '\t', '\n', '\r', '.');  // Trim trailing whitespace and periods

        return cleaned;
    }
}
