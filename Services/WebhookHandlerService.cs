using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for handling webhook events from Jellyfin.
/// </summary>
public class WebhookHandlerService
{
    private readonly JellyseerrApiService _apiService;
    private readonly ConfigurationService _configurationService;
    private readonly ILogger<WebhookHandlerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookHandlerService"/> class.
    /// </summary>
    /// <param name="apiService">The API service.</param>
    /// <param name="configurationService">The configuration service.</param>
    /// <param name="logger">The logger.</param>
    public WebhookHandlerService(JellyseerrApiService apiService, ConfigurationService configurationService, ILogger<WebhookHandlerService> logger)
    {
        _apiService = apiService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Handles webhook events from Jellyfin.
    /// </summary>
    /// <param name="webhookData">The webhook data.</param>
    /// <returns>True if handled successfully, false otherwise.</returns>
    public async Task<bool> HandleWebhookAsync(object webhookData)
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            
            if (!config.IsEnabled)
            {
                _logger.LogDebug("Plugin is disabled, ignoring webhook");
                return true;
            }

            _logger.LogDebug("Received webhook: {Data}", JsonConvert.SerializeObject(webhookData));

            // Parse webhook data
            var webhookJson = JsonConvert.SerializeObject(webhookData);
            var webhook = JsonConvert.DeserializeObject<JellyfinWebhook>(webhookJson);

            if (webhook == null)
            {
                _logger.LogWarning("Failed to parse webhook data");
                return false;
            }

            // Check if this is a favorite event for a series
            if (webhook.Favorite == true && webhook.ItemType == "Series")
            {
                _logger.LogInformation("Processing favorite event for series: {Name}", webhook.Name);
                return await HandleFavoriteSeriesAsync(webhook.Name);
            }

            _logger.LogDebug("Webhook event not relevant (Favorite: {Favorite}, ItemType: {ItemType})", 
                webhook.Favorite, webhook.ItemType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling webhook");
            return false;
        }
    }

    /// <summary>
    /// Handles a favorite series event.
    /// </summary>
    /// <param name="showName">The show name.</param>
    /// <returns>True if handled successfully, false otherwise.</returns>
    private async Task<bool> HandleFavoriteSeriesAsync(string showName)
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var masterFilePath = Path.Combine(config.ShowsDirectory, "show_paths.json");

            if (!File.Exists(masterFilePath))
            {
                _logger.LogWarning("Master show paths file does not exist: {Path}", masterFilePath);
                return false;
            }

            // Load master show paths
            var masterJson = await File.ReadAllTextAsync(masterFilePath);
            var masterShowPaths = JsonConvert.DeserializeObject<Dictionary<string, string>>(masterJson);

            if (masterShowPaths == null)
            {
                _logger.LogWarning("Failed to parse master show paths file");
                return false;
            }

            // Find the show's metadata file
            if (!masterShowPaths.TryGetValue(showName, out var jsonFilePath))
            {
                _logger.LogWarning("No metadata file found for show: {ShowName}", showName);
                return false;
            }

            if (!File.Exists(jsonFilePath))
            {
                _logger.LogWarning("Metadata file does not exist: {Path}", jsonFilePath);
                return false;
            }

            // Read show metadata
            var metadataJson = await File.ReadAllTextAsync(jsonFilePath);
            var metadata = JsonConvert.DeserializeObject<ShowMetadata>(metadataJson);

            if (metadata == null)
            {
                _logger.LogWarning("Failed to parse show metadata: {Path}", jsonFilePath);
                return false;
            }

            _logger.LogInformation("Found show metadata for {ShowName} (ID: {ShowId})", showName, metadata.ShowId);

            // Authenticate with Jellyseerr
            var authSuccess = await _apiService.AuthenticateAsync();
            if (!authSuccess)
            {
                _logger.LogError("Failed to authenticate with Jellyseerr");
                return false;
            }

            // Fetch detailed show information to get season count
            var showDetails = await _apiService.FetchShowDetailsAsync(metadata.ShowId);
            if (showDetails == null)
            {
                _logger.LogError("Failed to fetch show details for {ShowName} (ID: {ShowId})", showName, metadata.ShowId);
                return false;
            }

            var numberOfSeasons = showDetails.NumberOfSeasons;
            if (numberOfSeasons <= 0)
            {
                numberOfSeasons = 1; // Default to 1 season if not specified
                _logger.LogWarning("Show {ShowName} has no seasons specified, defaulting to 1", showName);
            }

            // Request the show
            var requestSuccess = await _apiService.RequestShowAsync(metadata.ShowId, numberOfSeasons);
            if (requestSuccess)
            {
                _logger.LogInformation("Successfully requested show {ShowName} with {Seasons} seasons", showName, numberOfSeasons);
                return true;
            }
            else
            {
                _logger.LogError("Failed to request show {ShowName}", showName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling favorite series {ShowName}", showName);
            return false;
        }
    }
}

/// <summary>
/// Jellyfin webhook model.
/// </summary>
public class JellyfinWebhook
{
    [JsonProperty("Favorite")]
    public bool? Favorite { get; set; }

    [JsonProperty("ItemType")]
    public string? ItemType { get; set; }

    [JsonProperty("Name")]
    public string? Name { get; set; }

    [JsonProperty("Id")]
    public string? Id { get; set; }

    [JsonProperty("UserId")]
    public string? UserId { get; set; }

    [JsonProperty("User")]
    public string? User { get; set; }

    [JsonProperty("ServerName")]
    public string? ServerName { get; set; }

    [JsonProperty("ServerVersion")]
    public string? ServerVersion { get; set; }

    [JsonProperty("NotificationType")]
    public string? NotificationType { get; set; }

    [JsonProperty("Timestamp")]
    public DateTime? Timestamp { get; set; }
}
