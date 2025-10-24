using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr MediaRequest objects that customizes the generated MediaRequest model
/// to match the actual API response structure from the TypeScript MediaRequest entity.
/// Only overrides properties where the type or JSON property name changes.
/// </summary>
public class JellyseerrMediaRequest : MediaRequest
{
    // Properties that need type changes from base class

    /// <summary>
    /// Media type as enum - override to use JsonStringEnumConverter
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public new MediaType Type { get; set; }
    
    // All other properties inherit from base class with correct types and JSON names
}
