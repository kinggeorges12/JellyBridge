using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyseerrBridge.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration() : this(
        jellyseerrUrl: string.Empty,
        apiKey: string.Empty,
        libraryDirectory: "/data/Jellyseerr",
        userId: 1)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    /// <param name="jellyseerrUrl">Jellyseerr base URL.</param>
    /// <param name="apiKey">Jellyseerr API key.</param>
    /// <param name="libraryDirectory">Library directory path.</param>
    /// <param name="userId">Jellyseerr user ID for requests.</param>
    public PluginConfiguration(
        string jellyseerrUrl,
        string apiKey,
        string libraryDirectory,
        int userId)
    {
        JellyseerrUrl = jellyseerrUrl;
        ApiKey = apiKey;
        LibraryDirectory = libraryDirectory;
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
    /// Gets or sets the Jellyseerr user ID for requests.
    /// </summary>
    [XmlElement("UserId")]
    public int UserId { get; set; }
}
