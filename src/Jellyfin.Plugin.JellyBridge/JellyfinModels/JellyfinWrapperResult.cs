namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Result of a Jellyfin wrapper operation (e.g., setting play status).
/// </summary>
public class JellyfinWrapperResult
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Message describing the result of the operation.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

