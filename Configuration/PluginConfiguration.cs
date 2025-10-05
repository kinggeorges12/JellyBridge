using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
/// </remarks>
/// <param name="jellyseerrUrl">Jellyseerr base URL.</param>
/// <param name="apiKey">Jellyseerr API key.</param>
/// <param name="libraryDirectory">Library directory path.</param>
/// <param name="createSeparateLibraries">Whether to create separate libraries for streaming services.</param>
/// <param name="libraryPrefix">Prefix for streaming service libraries.</param>
/// <param name="excludeFromMainLibraries">Whether to exclude placeholder shows from main libraries.</param>
/// <param name="isEnabled">Whether the plugin is enabled.</param>
/// <param name="syncIntervalHours">Sync interval in hours.</param>
/// <param name="webhookPort">Webhook port.</param>
/// <param name="userId">Jellyseerr user ID for requests.</param>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration() : this(
        jellyseerrUrl: string.Empty,
        apiKey: string.Empty,
        libraryDirectory: "/data/Jellyseerr",
        createSeparateLibraries: false,
        libraryPrefix: "Streaming - ",
        excludeFromMainLibraries: true,
        isEnabled: true,
        syncIntervalHours: 24,
        webhookPort: 5000,
        userId: 1)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    /// <param name="jellyseerrUrl">Jellyseerr base URL.</param>
    /// <param name="apiKey">Jellyseerr API key.</param>
    /// <param name="libraryDirectory">Library directory path.</param>
    /// <param name="createSeparateLibraries">Whether to create separate libraries for streaming services.</param>
    /// <param name="libraryPrefix">Prefix for streaming service libraries.</param>
    /// <param name="excludeFromMainLibraries">Whether to exclude placeholder shows from main libraries.</param>
    /// <param name="isEnabled">Whether the plugin is enabled.</param>
    /// <param name="syncIntervalHours">Sync interval in hours.</param>
    /// <param name="webhookPort">Webhook port.</param>
    /// <param name="userId">Jellyseerr user ID for requests.</param>
    public PluginConfiguration(
        string jellyseerrUrl,
        string apiKey,
        string libraryDirectory,
        bool createSeparateLibraries,
        string libraryPrefix,
        bool excludeFromMainLibraries,
        bool isEnabled,
        int syncIntervalHours,
        int webhookPort,
        int userId)
    {
        JellyseerrUrl = jellyseerrUrl;
        ApiKey = apiKey;
        LibraryDirectory = libraryDirectory;
        CreateSeparateLibraries = createSeparateLibraries;
        LibraryPrefix = libraryPrefix;
        ExcludeFromMainLibraries = excludeFromMainLibraries;
        IsEnabled = isEnabled;
        SyncIntervalHours = syncIntervalHours;
        WebhookPort = webhookPort;
        UserId = userId;
    }

    /// <summary>
    /// Gets or sets the Jellyseerr base URL.
    /// </summary>
    [Required]
    [XmlElement("JellyseerrUrl")]
    public string JellyseerrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    [Required]
    [XmlElement("ApiKey")]
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the library directory.
    /// </summary>
    [Required]
    [XmlElement("LibraryDirectory")]
    public string LibraryDirectory { get; set; }

    /// <summary>
    /// Gets or sets the services to fetch.
    /// </summary>
    [XmlArray("ServicesToFetch")]
    [XmlArrayItem("Service")]
    public List<string> ServicesToFetch { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to create separate libraries for streaming services.
    /// </summary>
    [XmlElement("CreateSeparateLibraries")]
    public bool CreateSeparateLibraries { get; set; }

    /// <summary>
    /// Gets or sets the prefix for streaming service libraries.
    /// </summary>
    [XmlElement("LibraryPrefix")]
    public string LibraryPrefix { get; set; }

    /// <summary>
    /// Gets or sets whether to exclude placeholder shows from main libraries.
    /// </summary>
    [XmlElement("ExcludeFromMainLibraries")]
    public bool ExcludeFromMainLibraries { get; set; }

    /// <summary>
    /// Gets or sets whether the plugin is enabled.
    /// </summary>
    [XmlElement("IsEnabled")]
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the sync interval in hours.
    /// </summary>
    [XmlElement("SyncIntervalHours")]
    public int SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets the webhook port.
    /// </summary>
    [XmlElement("WebhookPort")]
    public int WebhookPort { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr user ID for requests.
    /// </summary>
    [XmlElement("UserId")]
    public int UserId { get; set; }
}
