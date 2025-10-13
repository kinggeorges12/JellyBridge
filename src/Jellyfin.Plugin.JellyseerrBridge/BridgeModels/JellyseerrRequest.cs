using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr request that inherits from generated MediaRequest.
/// </summary>
public class JellyseerrRequest : MediaRequest
{
    // Additional properties for compatibility with existing code
    public string MediaTypeString => Type.ToString();
    
    // Additional properties that might be needed for compatibility
    // The base MediaRequest should contain most of the required properties
}
