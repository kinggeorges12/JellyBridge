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

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagementService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The plugin configuration.</param>
    public LibraryManagementService(ILogger<LibraryManagementService> logger, PluginConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
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
            var libraryPath = _configuration.ShowsDirectory;
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
        return _configuration.ShowsDirectory;
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

            _configuration.ShowsDirectory = newPath;
            _logger.LogInformation("Library directory updated to: {Path}", newPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update library directory path");
            return false;
        }
    }
}