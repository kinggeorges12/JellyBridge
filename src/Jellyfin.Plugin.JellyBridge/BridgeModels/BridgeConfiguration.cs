using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Bridge configuration classes and enums.
/// </summary>
public class BridgeConfiguration
{
    /// <summary>
    /// Defines the sort order options for discover library content.
    /// </summary>
    [JsonConverter(typeof(JsonNumberEnumConverter<SortOrderOptions>))]
    public enum SortOrderOptions
    {
        /// <summary>
        /// No sorting - sets play counts to zero for consistent ordering.
        /// </summary>
        None = 0,

        /// <summary>
        /// Random sorting - randomizes play counts for random ordering.
        /// </summary>
        Random = 1,

        /// <summary>
        /// Smart sorting - uses intelligent algorithm for optimal ordering (not yet implemented).
        /// </summary>
        Smart = 2
    }

    /// <summary>
    /// Returns the bridge configuration enums as JSON-serializable data.
    /// </summary>
    /// <returns>An anonymous object containing enum values for frontend consumption.</returns>
    public static object ToJson()
    {
        return new
        {
            SortOrderOptions = Enum.GetValues<SortOrderOptions>()
                .Select(e => new { Value = (int)e, Name = e.ToString() })
                .ToArray()
        };
    }
}

