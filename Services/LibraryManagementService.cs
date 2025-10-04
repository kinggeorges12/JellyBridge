using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing library configuration and recommendations.
/// </summary>
public class LibraryManagementService
{
    private readonly ConfigurationService _configurationService;
    private readonly ILogger<LibraryManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagementService"/> class.
    /// </summary>
    /// <param name="configurationService">The configuration service.</param>
    /// <param name="logger">The logger.</param>
    public LibraryManagementService(ConfigurationService configurationService, ILogger<LibraryManagementService> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets library configuration recommendations.
    /// </summary>
    /// <returns>Library configuration recommendations.</returns>
    public LibraryRecommendations GetLibraryRecommendations()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var recommendations = new LibraryRecommendations();

            if (config.CreateSeparateLibraries)
            {
                foreach (var serviceName in config.ServicesToFetch)
                {
                    if (config.ServiceDirectories.TryGetValue(serviceName, out var serviceDirectory))
                    {
                        var libraryName = $"{config.LibraryPrefix}{serviceName}";
                        recommendations.RecommendedLibraries.Add(new LibraryRecommendation
                        {
                            Name = libraryName,
                            Path = serviceDirectory,
                            Type = "TV Shows",
                            Description = $"Streaming shows from {serviceName}",
                            ServiceName = serviceName
                        });
                    }
                }
            }

            if (config.ExcludeFromMainLibraries)
            {
                recommendations.ExcludePaths.AddRange(config.ServiceDirectories.Values);
            }

            _logger.LogInformation("Generated library recommendations for {ServiceCount} services", 
                config.ServicesToFetch.Count);

            return recommendations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating library recommendations");
            return new LibraryRecommendations();
        }
    }

    /// <summary>
    /// Validates library configuration.
    /// </summary>
    /// <returns>Validation results.</returns>
    public LibraryValidationResult ValidateLibraryConfiguration()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var result = new LibraryValidationResult();

            // Check if service directories exist and are accessible
            foreach (var kvp in config.ServiceDirectories)
            {
                var serviceName = kvp.Key;
                var directoryPath = kvp.Value;

                try
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        result.Warnings.Add($"Directory for {serviceName} does not exist: {directoryPath}");
                    }
                    else
                    {
                        // Test write access
                        var testFile = Path.Combine(directoryPath, ".test_write_access");
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Cannot access directory for {serviceName}: {ex.Message}");
                }
            }

            // Check for potential conflicts with main libraries
            if (config.ExcludeFromMainLibraries)
            {
                foreach (var serviceDirectory in config.ServiceDirectories.Values)
                {
                    if (serviceDirectory.Contains("/Movies/") || serviceDirectory.Contains("/TV Shows/"))
                    {
                        result.Warnings.Add($"Service directory {serviceDirectory} may conflict with main libraries. Consider using separate paths.");
                    }
                }
            }

            result.IsValid = result.Errors.Count == 0;
            _logger.LogInformation("Library validation completed. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}", 
                result.IsValid, result.Errors.Count, result.Warnings.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating library configuration");
            return new LibraryValidationResult
            {
                IsValid = false,
                Errors = { "Error validating library configuration" }
            };
        }
    }
}

/// <summary>
/// Library recommendations model.
/// </summary>
public class LibraryRecommendations
{
    /// <summary>
    /// Gets or sets the recommended libraries.
    /// </summary>
    public List<LibraryRecommendation> RecommendedLibraries { get; set; } = new();

    /// <summary>
    /// Gets or sets the paths to exclude from main libraries.
    /// </summary>
    public List<string> ExcludePaths { get; set; } = new();
}

/// <summary>
/// Library recommendation model.
/// </summary>
public class LibraryRecommendation
{
    /// <summary>
    /// Gets or sets the library name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service name.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;
}

/// <summary>
/// Library validation result model.
/// </summary>
public class LibraryValidationResult
{
    /// <summary>
    /// Gets or sets whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the validation errors.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Gets or sets the validation warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
