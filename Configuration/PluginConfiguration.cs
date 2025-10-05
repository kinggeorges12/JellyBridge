using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration
{
    /// <summary>
    /// Gets or sets the Jellyseerr base URL.
    /// </summary>
    [Required]
    public string JellyseerrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyseerr email for authentication.
    /// </summary>
    [Required]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyseerr password for authentication.
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base directory for shows.
    /// </summary>
    [Required]
    public string ShowsDirectory { get; set; } = "/data/Jellyseerr";

    /// <summary>
    /// Gets or sets the service directories configuration.
    /// </summary>
    public Dictionary<string, string> ServiceDirectories { get; set; } = new();

    /// <summary>
    /// Gets or sets the service IDs mapping.
    /// </summary>
    public Dictionary<string, int> ServiceIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the services to fetch.
    /// </summary>
    public List<string> ServicesToFetch { get; set; } = new();

    /// <summary>
    /// Gets or sets the sync interval in hours.
    /// </summary>
    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets whether the plugin is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the webhook port.
    /// </summary>
    public int WebhookPort { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the user ID for requests.
    /// </summary>
    public int UserId { get; set; } = 1;

    /// <summary>
    /// Gets or sets the root folder for downloads.
    /// </summary>
    public string RootFolder { get; set; } = "/data/Jellyseerr";

    /// <summary>
    /// Gets or sets whether to request 4K content.
    /// </summary>
    public bool Request4K { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to create separate libraries for streaming services.
    /// </summary>
    public bool CreateSeparateLibraries { get; set; } = true;

    /// <summary>
    /// Gets or sets the prefix for streaming service libraries.
    /// </summary>
    public string LibraryPrefix { get; set; } = "Streaming - ";

    /// <summary>
    /// Gets or sets whether to exclude placeholder shows from main libraries.
    /// </summary>
    public bool ExcludeFromMainLibraries { get; set; } = true;
}
