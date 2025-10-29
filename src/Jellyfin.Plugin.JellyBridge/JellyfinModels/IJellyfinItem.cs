namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Common interface for Jellyfin item wrappers.
/// Provides shared functionality for all Jellyfin item wrappers.
/// </summary>
public interface IJellyfinItem
{
    /// <summary>
    /// Get the ID of this item.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Get the name of this item.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Get the path of this item.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Get the provider IDs for this item.
    /// </summary>
    Dictionary<string, string> ProviderIds { get; }

    /// <summary>
    /// Extract TMDB ID from item metadata.
    /// </summary>
    /// <returns>TMDB ID if found, null otherwise</returns>
    int? GetTmdbId();

    /// <summary>
    /// Get a provider ID by name.
    /// </summary>
    /// <param name="name">The provider name</param>
    /// <returns>The provider ID if found, null otherwise</returns>
    string? GetProviderId(string name);

    /// <summary>
    /// Check if two items match by comparing IDs.
    /// </summary>
    /// <param name="other">Other item to compare</param>
    /// <returns>True if the items match, false otherwise</returns>
    bool ItemsMatch(IJellyfinItem other);

    /// <summary>
    /// Serialize the item to JSON using the provided DTO service.
    /// </summary>
    /// <param name="dtoService">The DTO service to use for serialization</param>
    /// <returns>JSON representation of the item</returns>
    string? ToJson(MediaBrowser.Controller.Dto.IDtoService dtoService);
}
