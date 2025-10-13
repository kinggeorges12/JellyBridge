namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Response type enumeration for API calls.
/// Maps to specific JellyseerrModel classes and bridge models.
/// 
/// This enum defines how different API responses should be deserialized,
/// ensuring type safety and proper handling of different response structures.
/// </summary>
public enum JellyseerrResponseType
{
    // Bridge model specific types that map to JellyseerrModel classes
    /// <summary>
    /// Uses JellyseerrPaginatedResponse&lt;T&gt; bridge model for discover endpoints.
    /// </summary>
    DiscoverResponse,
    
    /// <summary>
    /// Uses JellyseerrUser bridge model (maps to JellyseerrModel.Server.User).
    /// </summary>
    UserResponse,
    
    /// <summary>
    /// Uses JellyseerrPaginatedResponse&lt;JellyseerrRequest&gt; bridge model (maps to JellyseerrModel.Server.MediaRequest).
    /// </summary>
    RequestResponse,
    
    /// <summary>
    /// Uses JellyseerrWatchNetwork/JellyseerrWatchRegion bridge models (maps to JellyseerrModel.TmdbNetwork/TmdbRegion).
    /// </summary>
    WatchProviderResponse,
    
    /// <summary>
    /// Uses SystemStatus from JellyseerrModel for status endpoint.
    /// </summary>
    StatusResponse
}
