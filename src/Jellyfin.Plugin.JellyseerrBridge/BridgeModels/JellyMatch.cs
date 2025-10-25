using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Represents a match between a Jellyseerr item and a Jellyfin item.
/// </summary>
/// <typeparam name="IJellyseerrItem">The type of Jellyseerr item</typeparam>
public class JellyMatch
{
    /// <summary>
    /// The Jellyseerr item that matches the Jellyfin item.
    /// </summary>
    public IJellyseerrItem JellyseerrItem { get; set; }

    /// <summary>
    /// The Jellyfin item that matches the Jellyseerr item.
    /// </summary>
    public BaseItem JellyfinItem { get; set; }

    /// <summary>
    /// Initializes a new instance of the JellyMatch class.
    /// </summary>
    /// <param name="jellyseerrItem">The Jellyseerr item</param>
    /// <param name="jellyfinItem">The Jellyfin item</param>
    public JellyMatch(IJellyseerrItem jellyseerrItem, BaseItem jellyfinItem)
    {
        JellyseerrItem = jellyseerrItem ?? throw new ArgumentNullException(nameof(jellyseerrItem));
        JellyfinItem = jellyfinItem ?? throw new ArgumentNullException(nameof(jellyfinItem));
    }

    /// <summary>
    /// Creates a JellyMatch from a tuple.
    /// </summary>
    /// <param name="match">The tuple containing the Jellyseerr and Jellyfin items</param>
    /// <returns>A new JellyMatch instance</returns>
    public static JellyMatch FromTuple((IJellyseerrItem bridgeItem, BaseItem jellyfinItem) match)
    {
        return new JellyMatch(match.bridgeItem, match.jellyfinItem);
    }

    /// <summary>
    /// Converts the JellyMatch to a tuple.
    /// </summary>
    /// <returns>A tuple containing the Jellyseerr and Jellyfin items</returns>
    public (IJellyseerrItem bridgeItem, BaseItem jellyfinItem) ToTuple()
    {
        return (JellyseerrItem, JellyfinItem);
    }
}
