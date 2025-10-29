using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

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

    /// <summary>
    /// Media as Media object ignored in json output - override to use JellyseerrMedia
    /// </summary>
    //[JsonIgnore]
    //public new Media Media { get; set; } = new();
    [JsonPropertyName("media")]
    public new JellyseerrMedia Media { get; set; } = new();
    
    // All other properties inherit from base class with correct types and JSON names
    
    /// <summary>
    /// Tests if this request matches an IJellyfinItem by comparing TMDB IDs and media types.
    /// </summary>
    /// <param name="jellyfinItem">The IJellyfinItem to compare against</param>
    /// <returns>True if the request matches the IJellyfinItem, false otherwise</returns>
    public bool EqualsItem(IJellyfinItem jellyfinItem)
    {
        if (jellyfinItem == null || Media == null)
            return false;
            
        // Compare media types first
        var jellyfinType = jellyfinItem switch
        {
            JellyfinMovie => MediaType.MOVIE,
            JellyfinSeries => MediaType.TV,
            _ => throw new NotSupportedException($"Unsupported item type: {jellyfinItem.GetType().Name}")
        };
        if (Type != jellyfinType)
            return false;
            
        // Get TMDB ID from Jellyfin item
        int? jellyfinTmdbId = null;
        // Try to get TMDB ID from provider IDs
        if (jellyfinItem.ProviderIds.TryGetValue("Tmdb", out var providerId) && !string.IsNullOrEmpty(providerId))
        {
            if (int.TryParse(providerId, out var id))
            {
                jellyfinTmdbId = id;
            }
        }
        if (!jellyfinTmdbId.HasValue)
            return false;
            
        // Compare TMDB IDs
        return Media.Id == jellyfinTmdbId.Value;
    }
}
