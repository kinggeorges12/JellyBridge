namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Common interface for all Jellyseerr request models.
/// This allows the factory to accept any type of request model in a type-safe way.
/// </summary>
public interface IJellyseerrRequest
{
    /// <summary>
    /// Gets template values for URL path placeholders (e.g., user ID for /api/v1/user/{id}/quota).
    /// </summary>
    Dictionary<string, string>? GetTemplateValues();
}
