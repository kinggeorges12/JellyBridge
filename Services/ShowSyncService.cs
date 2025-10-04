using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for syncing shows and creating placeholder directories.
/// </summary>
public class ShowSyncService
{
    private readonly JellyseerrApiService _apiService;
    private readonly ConfigurationService _configurationService;
    private readonly ILogger<ShowSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShowSyncService"/> class.
    /// </summary>
    /// <param name="apiService">The API service.</param>
    /// <param name="configurationService">The configuration service.</param>
    /// <param name="logger">The logger.</param>
    public ShowSyncService(JellyseerrApiService apiService, ConfigurationService configurationService, ILogger<ShowSyncService> logger)
    {
        _apiService = apiService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Syncs all shows for configured services.
    /// </summary>
    /// <returns>True if sync successful, false otherwise.</returns>
    public async Task<bool> SyncAllShowsAsync()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            
            if (!config.IsEnabled)
            {
                _logger.LogInformation("Plugin is disabled, skipping sync");
                return true;
            }

            if (!_configurationService.ValidateConfiguration(config))
            {
                _logger.LogError("Configuration validation failed, cannot sync shows");
                return false;
            }

            _logger.LogInformation("Starting show sync for {ServiceCount} services", config.ServicesToFetch.Count);

            var masterShowPaths = new Dictionary<string, string>();

            foreach (var serviceName in config.ServicesToFetch)
            {
                if (!config.ServiceIds.TryGetValue(serviceName, out var networkId))
                {
                    _logger.LogWarning("No network ID found for service {ServiceName}, skipping", serviceName);
                    continue;
                }

                if (!config.ServiceDirectories.TryGetValue(serviceName, out var serviceDirectory))
                {
                    _logger.LogWarning("No directory found for service {ServiceName}, skipping", serviceName);
                    continue;
                }

                _logger.LogInformation("Syncing shows for service {ServiceName} (Network ID: {NetworkId})", serviceName, networkId);
                
                var serviceShowPaths = await SyncServiceShowsAsync(serviceName, networkId, serviceDirectory);
                foreach (var kvp in serviceShowPaths)
                {
                    masterShowPaths[kvp.Key] = kvp.Value;
                }
            }

            // Save master show paths file
            await SaveMasterShowPathsAsync(masterShowPaths);
            
            _logger.LogInformation("Show sync completed successfully. Total shows: {Count}", masterShowPaths.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during show sync");
            return false;
        }
    }

    /// <summary>
    /// Syncs shows for a specific service.
    /// </summary>
    /// <param name="serviceName">The service name.</param>
    /// <param name="networkId">The network ID.</param>
    /// <param name="serviceDirectory">The service directory path.</param>
    /// <returns>Dictionary of show names to their JSON file paths.</returns>
    private async Task<Dictionary<string, string>> SyncServiceShowsAsync(string serviceName, int networkId, string serviceDirectory)
    {
        var showPaths = new Dictionary<string, string>();

        try
        {
            // Create service directory if it doesn't exist
            Directory.CreateDirectory(serviceDirectory);
            _logger.LogDebug("Created/verified service directory: {Directory}", serviceDirectory);

            // Fetch shows from Jellyseerr
            var shows = await _apiService.FetchShowsAsync(networkId);
            
            if (shows.Count == 0)
            {
                _logger.LogWarning("No shows found for service {ServiceName}", serviceName);
                return showPaths;
            }

            _logger.LogInformation("Found {Count} shows for service {ServiceName}", shows.Count, serviceName);

            // Create directories and metadata files for each show
            foreach (var show in shows)
            {
                try
                {
                    var sanitizedName = SanitizeName(show.Name);
                    var showDirPath = Path.Combine(serviceDirectory, sanitizedName);
                    
                    // Create show directory
                    Directory.CreateDirectory(showDirPath);
                    
                    // Create metadata file
                    var metadataFilePath = Path.Combine(showDirPath, $"{sanitizedName}.json");
                    var metadata = new ShowMetadata
                    {
                        Name = show.Name,
                        Overview = show.Overview ?? "No overview available",
                        ShowId = show.Id,
                        Year = ExtractYear(show.FirstAirDate),
                        Service = serviceName,
                        NetworkId = networkId
                    };

                    var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                    await File.WriteAllTextAsync(metadataFilePath, json, Encoding.UTF8);

                    // Create .nomedia file to prevent Jellyfin from scanning this directory
                    var noMediaFilePath = Path.Combine(showDirPath, ".nomedia");
                    await File.WriteAllTextAsync(noMediaFilePath, string.Empty);

                    // Create a placeholder season directory with .nomedia to prevent scanning
                    var seasonDirPath = Path.Combine(showDirPath, "Season 1");
                    Directory.CreateDirectory(seasonDirPath);
                    var seasonNoMediaPath = Path.Combine(seasonDirPath, ".nomedia");
                    await File.WriteAllTextAsync(seasonNoMediaPath, string.Empty);
                    
                    showPaths[show.Name] = metadataFilePath;
                    
                    _logger.LogDebug("Created placeholder for show: {ShowName}", show.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating placeholder for show {ShowName}", show.Name);
                }
            }

            _logger.LogInformation("Successfully created {Count} placeholders for service {ServiceName}", showPaths.Count, serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing shows for service {ServiceName}", serviceName);
        }

        return showPaths;
    }

    /// <summary>
    /// Saves the master show paths file.
    /// </summary>
    /// <param name="masterShowPaths">The master show paths dictionary.</param>
    private async Task SaveMasterShowPathsAsync(Dictionary<string, string> masterShowPaths)
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var masterFilePath = Path.Combine(config.ShowsDirectory, "show_paths.json");
            
            // Ensure shows directory exists
            Directory.CreateDirectory(config.ShowsDirectory);
            
            var json = JsonConvert.SerializeObject(masterShowPaths, Formatting.Indented);
            await File.WriteAllTextAsync(masterFilePath, json, Encoding.UTF8);
            
            _logger.LogInformation("Saved master show paths file with {Count} entries", masterShowPaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving master show paths file");
        }
    }

    /// <summary>
    /// Sanitizes a name to be filesystem safe.
    /// </summary>
    /// <param name="name">The name to sanitize.</param>
    /// <returns>The sanitized name.</returns>
    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unknown";

        // Remove or replace invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();
        
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '_' || c == '-')
            {
                sanitized.Append(c);
            }
            else if (invalidChars.Contains(c))
            {
                sanitized.Append('_');
            }
        }

        return sanitized.ToString().Trim();
    }

    /// <summary>
    /// Extracts the year from a date string.
    /// </summary>
    /// <param name="dateString">The date string.</param>
    /// <returns>The year as a string, or empty string if not found.</returns>
    private static string ExtractYear(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return string.Empty;

        if (DateTime.TryParse(dateString, out var date))
        {
            return date.Year.ToString();
        }

        // Try to extract year from YYYY-MM-DD format
        if (dateString.Length >= 4 && int.TryParse(dateString.Substring(0, 4), out var year))
        {
            return year.ToString();
        }

        return string.Empty;
    }
}

/// <summary>
/// Show metadata model.
/// </summary>
public class ShowMetadata
{
    [JsonProperty("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("Overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonProperty("Show ID")]
    public int ShowId { get; set; }

    [JsonProperty("Year")]
    public string Year { get; set; } = string.Empty;

    [JsonProperty("Service")]
    public string Service { get; set; } = string.Empty;

    [JsonProperty("NetworkId")]
    public int NetworkId { get; set; }
}
