using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing libraries.
/// </summary>
public class LibraryManagementService
{
    private readonly ILogger<LibraryManagementService> _logger;
    private readonly PluginConfiguration _configuration;
    private readonly LibraryFilterService _libraryFilterService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagementService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="libraryFilterService">The library filter service.</param>
    public LibraryManagementService(ILogger<LibraryManagementService> logger, PluginConfiguration configuration, LibraryFilterService libraryFilterService)
    {
        _logger = logger;
        _configuration = configuration;
        _libraryFilterService = libraryFilterService;
    }

    /// <summary>
    /// Gets library recommendations.
    /// </summary>
    /// <returns>Library recommendations.</returns>
    public object GetLibraryRecommendations()
    {
        _logger.LogInformation("Getting library recommendations");
        return new { message = "Library recommendations not yet implemented" };
    }

    /// <summary>
    /// Validates library configuration.
    /// </summary>
    /// <returns>Validation result.</returns>
    public object ValidateLibraryConfiguration()
    {
        _logger.LogInformation("Validating library configuration");
        return new { isValid = true, message = "Library validation not yet implemented" };
    }

        /// <summary>
        /// Ensures the Jellyseerr library directory exists.
        /// </summary>
        /// <returns>True if directory exists or was created successfully.</returns>
        public bool EnsureLibraryDirectoryExists()
        {
            try
            {
                var libraryPath = _configuration.LibraryDirectory;
                if (string.IsNullOrEmpty(libraryPath))
                {
                    _logger.LogError("Library directory path is not configured");
                    return false;
                }

                if (!Directory.Exists(libraryPath))
                {
                    _logger.LogInformation("Creating Jellyseerr library directory: {Path}", libraryPath);
                    Directory.CreateDirectory(libraryPath);
                    _logger.LogInformation("Jellyseerr library directory created successfully");
                }
                else
                {
                    _logger.LogInformation("Jellyseerr library directory already exists: {Path}", libraryPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure library directory exists");
                return false;
            }
        }

        /// <summary>
        /// Gets the current library directory path.
        /// </summary>
        /// <returns>The library directory path.</returns>
        public string GetLibraryDirectory()
        {
            return _configuration.LibraryDirectory;
        }

        /// <summary>
        /// Updates the library directory path.
        /// </summary>
        /// <param name="newPath">The new library directory path.</param>
        /// <returns>True if the path was updated successfully.</returns>
        public bool UpdateLibraryDirectory(string newPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath))
                {
                    _logger.LogError("Cannot set empty library directory path");
                    return false;
                }

                _configuration.LibraryDirectory = newPath;
                _logger.LogInformation("Library directory updated to: {Path}", newPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update library directory path");
                return false;
            }
        }

        /// <summary>
        /// Filters shows based on plugin configuration.
        /// </summary>
        /// <param name="showPaths">List of show paths to filter.</param>
        /// <param name="targetLibraryType">The type of library being processed.</param>
        /// <returns>Filtered list of show paths.</returns>
        public List<string> FilterShows(List<string> showPaths, LibraryType targetLibraryType)
        {
            try
            {
                _logger.LogInformation("Filtering {Count} shows for {LibraryType} library", showPaths.Count, targetLibraryType);
                
                var filteredShows = new List<string>();
                
                foreach (var showPath in showPaths)
                {
                    if (_libraryFilterService.ShouldIncludeInLibrary(showPath, targetLibraryType))
                    {
                        filteredShows.Add(showPath);
                        _logger.LogDebug("Including show: {Path}", showPath);
                    }
                    else
                    {
                        _logger.LogDebug("Excluding show: {Path}", showPath);
                    }
                }
                
                _logger.LogInformation("Filtered {OriginalCount} shows to {FilteredCount} shows", showPaths.Count, filteredShows.Count);
                return filteredShows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering shows");
                return showPaths; // Return original list on error
            }
        }

        /// <summary>
        /// Checks if a show should be excluded from main libraries.
        /// </summary>
        /// <param name="showPath">The path to the show.</param>
        /// <returns>True if the show should be excluded.</returns>
        public bool ShouldExcludeFromMainLibraries(string showPath)
        {
            return _libraryFilterService.ShouldExcludeFromMainLibraries(showPath);
        }

        /// <summary>
        /// Checks if a show is a placeholder (has no actual media content).
        /// </summary>
        /// <param name="showPath">The path to the show.</param>
        /// <returns>True if the show is a placeholder.</returns>
        public bool IsPlaceholderShow(string showPath)
        {
            return _libraryFilterService.IsPlaceholderShow(showPath);
        }

        /// <summary>
        /// Gets the library type for a show.
        /// </summary>
        /// <param name="showPath">The path to the show.</param>
        /// <returns>The library type.</returns>
        public LibraryType GetLibraryType(string showPath)
        {
            return _libraryFilterService.GetLibraryType(showPath);
        }
}