using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for filtering library content based on plugin configuration.
/// </summary>
public class LibraryFilterService
{
    private readonly ILogger<LibraryFilterService> _logger;
    private readonly PluginConfiguration _configuration;

    /// <summary>
    /// Media file extensions that indicate actual content.
    /// </summary>
    private static readonly string[] MediaExtensions = 
    {
        ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".flv", 
        ".mp3", ".flac", ".aac", ".ogg", ".wav", ".m4a",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff"
    };

    /// <summary>
    /// Placeholder file extensions that don't indicate actual content.
    /// </summary>
    private static readonly string[] PlaceholderExtensions = 
    {
        ".nfo", ".txt", ".url", ".lnk", ".desktop", ".webloc"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryFilterService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The plugin configuration.</param>
    public LibraryFilterService(ILogger<LibraryFilterService> logger, PluginConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Determines if a show should be excluded from main libraries.
    /// </summary>
    /// <param name="showPath">The path to the show directory.</param>
    /// <param name="isStreamingLibrary">Whether this is a dedicated streaming library.</param>
    /// <returns>True if the show should be excluded from main libraries.</returns>
    public bool ShouldExcludeFromMainLibraries(string showPath, bool isStreamingLibrary = false)
    {
        try
        {
            // If exclusion is disabled, never exclude
            if (!_configuration.ExcludeFromMainLibraries)
            {
                return false;
            }

            // If this is already a streaming library, don't exclude
            if (isStreamingLibrary)
            {
                return false;
            }

            // Check if this is a placeholder show
            return IsPlaceholderShow(showPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if show should be excluded: {Path}", showPath);
            return false; // Default to not excluding on error
        }
    }

    /// <summary>
    /// Determines if a show is a placeholder (has no actual media content).
    /// </summary>
    /// <param name="showPath">The path to the show directory.</param>
    /// <returns>True if the show is a placeholder.</returns>
    public bool IsPlaceholderShow(string showPath)
    {
        try
        {
            if (string.IsNullOrEmpty(showPath) || !Directory.Exists(showPath))
            {
                _logger.LogDebug("Show path does not exist: {Path}", showPath);
                return true; // Non-existent paths are considered placeholders
            }

            // Get all files in the show directory and subdirectories
            var allFiles = Directory.GetFiles(showPath, "*.*", SearchOption.AllDirectories);
            
            if (allFiles.Length == 0)
            {
                _logger.LogDebug("Show directory is empty: {Path}", showPath);
                return true; // Empty directories are placeholders
            }

            // Count media files vs placeholder files
            int mediaFileCount = 0;
            int placeholderFileCount = 0;

            foreach (var file in allFiles)
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                
                if (MediaExtensions.Contains(extension))
                {
                    mediaFileCount++;
                }
                else if (PlaceholderExtensions.Contains(extension))
                {
                    placeholderFileCount++;
                }
            }

            // If we have media files, it's not a placeholder
            if (mediaFileCount > 0)
            {
                _logger.LogDebug("Show has {MediaCount} media files: {Path}", mediaFileCount, showPath);
                return false;
            }

            // If we only have placeholder files or other files, it's a placeholder
            _logger.LogDebug("Show is placeholder (media: {MediaCount}, placeholder: {PlaceholderCount}): {Path}", 
                mediaFileCount, placeholderFileCount, showPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if show is placeholder: {Path}", showPath);
            return false; // Default to not being a placeholder on error
        }
    }

    /// <summary>
    /// Gets the library type for a given show path.
    /// </summary>
    /// <param name="showPath">The path to the show directory.</param>
    /// <returns>The library type (Main, Streaming, or Unknown).</returns>
    public LibraryType GetLibraryType(string showPath)
    {
        try
        {
            if (string.IsNullOrEmpty(showPath))
            {
                return LibraryType.Unknown;
            }

            // Check if this is in a streaming service directory
            var directoryName = Path.GetDirectoryName(showPath)?.ToLowerInvariant() ?? "";
            var showName = Path.GetFileName(showPath)?.ToLowerInvariant() ?? "";

            // Check for streaming service indicators
            var streamingServices = new[] { "netflix", "prime", "disney", "hulu", "hbo", "paramount", "peacock", "apple" };
            
            foreach (var service in streamingServices)
            {
                if (directoryName.Contains(service) || showName.Contains(service))
                {
                    return LibraryType.Streaming;
                }
            }

            // Check if it's in the Jellyseerr library directory
            if (showPath.StartsWith(_configuration.LibraryDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return LibraryType.Streaming;
            }

            return LibraryType.Main;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining library type: {Path}", showPath);
            return LibraryType.Unknown;
        }
    }

    /// <summary>
    /// Determines if a show should be included in a specific library.
    /// </summary>
    /// <param name="showPath">The path to the show directory.</param>
    /// <param name="targetLibraryType">The type of library being processed.</param>
    /// <returns>True if the show should be included in the target library.</returns>
    public bool ShouldIncludeInLibrary(string showPath, LibraryType targetLibraryType)
    {
        try
        {
            var showLibraryType = GetLibraryType(showPath);
            var isPlaceholder = IsPlaceholderShow(showPath);

            // Always include non-placeholder shows
            if (!isPlaceholder)
            {
                return true;
            }

            // For placeholder shows, apply exclusion rules
            switch (targetLibraryType)
            {
                case LibraryType.Main:
                    // Exclude placeholders from main libraries if configured
                    return !_configuration.ExcludeFromMainLibraries;
                
                case LibraryType.Streaming:
                    // Always include placeholders in streaming libraries
                    return true;
                
                default:
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining if show should be included: {Path}", showPath);
            return true; // Default to including on error
        }
    }
}

/// <summary>
/// Represents the type of library.
/// </summary>
public enum LibraryType
{
    /// <summary>
    /// Main library (Movies, TV Shows, etc.).
    /// </summary>
    Main,

    /// <summary>
    /// Streaming service library.
    /// </summary>
    Streaming,

    /// <summary>
    /// Unknown library type.
    /// </summary>
    Unknown
}
