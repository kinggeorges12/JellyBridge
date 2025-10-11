namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Test connection request model for configuration testing.
/// </summary>
public class TestConnectionRequest
{
    /// <summary>
    /// Gets or sets the Jellyseerr URL to test.
    /// </summary>
    public string JellyseerrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key to test.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
