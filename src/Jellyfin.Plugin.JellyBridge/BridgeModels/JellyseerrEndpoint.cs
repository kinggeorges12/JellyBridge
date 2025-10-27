namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Enumeration of Jellyseerr API endpoints based on actual TypeScript route definitions.
/// 
/// This enum centralizes all Jellyseerr API endpoints used by the bridge,
/// making it easy to add new endpoints and maintain consistency.
/// 
/// SOURCE: Based on actual TypeScript route definitions in the Jellyseerr codebase:
/// - server/routes/index.ts
/// - server/routes/request.ts  
/// - server/routes/discover.ts
/// - server/routes/auth.ts
/// </summary>
public enum JellyseerrEndpoint
{
    // Status endpoints (from server/routes/index.ts)
    Status,
    
    // Request endpoints (from server/routes/request.ts)
    ReadRequests,
    CreateRequest,
    
    // Discover endpoints (from server/routes/discover.ts)
    DiscoverMovies,
    DiscoverTv,
    
    // Auth endpoints (from server/routes/auth.ts)
    AuthMe,
    
    // Watch provider endpoints (from server/routes/index.ts)
    WatchProvidersRegions,
    WatchProvidersMovies,
    WatchProvidersTv,
    
    // Additional endpoints that might be useful
    UserList
}
