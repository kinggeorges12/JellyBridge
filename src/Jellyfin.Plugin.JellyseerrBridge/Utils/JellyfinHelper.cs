using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Jellyfin.Data.Enums;
using JellyfinUser = Jellyfin.Data.Entities.User;

namespace Jellyfin.Plugin.JellyseerrBridge.Utils;

/// <summary>
/// Static helper class for Jellyfin operations.
/// </summary>
public static class JellyfinHelper
{
    /// <summary>
    /// Get all favorites for all users from Jellyfin.
    /// </summary>
    /// <param name="userManager">The user manager</param>
    /// <param name="libraryManager">The library manager</param>
    /// <param name="userDataManager">The user data manager</param>
    /// <returns>Dictionary mapping users to their favorite items</returns>
    public static Dictionary<JellyfinUser, List<BaseItem>> GetUserFavorites(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager)
    {
        var userFavorites = new Dictionary<JellyfinUser, List<BaseItem>>();
        
        try
        {
            // Get all users from user manager
            var users = userManager.Users.ToList();
            
            // Get all items from libraries
            var allItems = libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true
            });
            
            // Initialize empty lists for each user
            foreach (var user in users)
            {
                userFavorites[user] = new List<BaseItem>();
            }
            
            // Check favorites for each user
            foreach (var user in users)
            {
                try
                {
                    var userFavs = new List<BaseItem>();
                    
                    foreach (var item in allItems)
                    {
                        try
                        {
                            // Check if item is favorited by user
                            var userData = userDataManager.GetUserData(user, item);
                            if (userData.IsFavorite)
                            {
                                userFavs.Add(item);
                            }
                        }
                        catch
                        {
                            // Ignore errors and continue
                        }
                    }
                    
                    userFavorites[user] = userFavs;
                }
                catch
                {
                    // Ignore errors and continue with empty list
                    userFavorites[user] = new List<BaseItem>();
                }
            }
        }
        catch
        {
            // Ignore errors and return empty dictionary
        }
        
        return userFavorites;
    }
    
    /// <summary>
    /// Get all existing items of a specific type from Jellyfin libraries.
    /// </summary>
    /// <typeparam name="T">The type of items to retrieve</typeparam>
    /// <param name="libraryManager">The library manager</param>
    /// <param name="libraryPath">Optional library path to filter items. If provided, only items in this path will be returned.</param>
    /// <returns>List of existing items</returns>
    public static List<T> GetExistingItems<T>(ILibraryManager libraryManager, string? libraryPath = null) where T : BaseItem
    {
        try
        {
            // Get the appropriate BaseItemKind for the type
            BaseItemKind[] itemTypes;
            if (typeof(T) == typeof(Movie))
            {
                itemTypes = new[] { BaseItemKind.Movie };
            }
            else if (typeof(T) == typeof(Series))
            {
                itemTypes = new[] { BaseItemKind.Series };
            }
            else
            {
                return new List<T>();
            }
            
            var items = libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = itemTypes,
                Recursive = true
            });
            
            var filteredItems = items.OfType<T>();
            
            // Filter by library path if provided
            if (!string.IsNullOrEmpty(libraryPath))
            {
                filteredItems = filteredItems.Where(item => 
                    !string.IsNullOrEmpty(item.Path) && 
                    item.Path.StartsWith(libraryPath, StringComparison.OrdinalIgnoreCase));
            }
            
            return filteredItems.ToList();
        }
        catch
        {
            return new List<T>();
        }
    }
    
    /// <summary>
    /// Extract TMDB ID from item metadata.
    /// </summary>
    /// <param name="item">The item to extract TMDB ID from</param>
    /// <returns>TMDB ID if found, null otherwise</returns>
    public static int? GetTmdbId(BaseItem item)
    {
        try
        {
            // Try to get TMDB ID from provider IDs
            if (item.ProviderIds.TryGetValue("Tmdb", out var providerId) && !string.IsNullOrEmpty(providerId))
            {
                if (int.TryParse(providerId, out var id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // Ignore errors and return null
        }
        
        return null;
    }

    /// <summary>
    /// Check if two BaseItem objects match by comparing IDs and media types.
    /// </summary>
    /// <param name="item1">First BaseItem to compare</param>
    /// <param name="item2">Second BaseItem to compare</param>
    /// <returns>True if the items match, false otherwise</returns>
    public static bool ItemsMatch(BaseItem item1, BaseItem item2)
    {
        if (item1 == null || item2 == null) return false;
        
        // Check item IDs first
        if (item1.Id != item2.Id) return false;
        
        // Then check media types match
        return (item1 is Movie && item2 is Movie) || (item1 is Series && item2 is Series);
    }
}

/// <summary>
/// Maps between Jellyfin item types, collection type enums, and BaseItemKind enums.
/// </summary>
public class JellyfinTypeMapping
{
    // BaseItemKind constants
    public static readonly BaseItemKind MovieKind = BaseItemKind.Movie;
    public static readonly BaseItemKind SeriesKind = BaseItemKind.Series;

    public static bool IsLibraryTypeCompatible<T>(CollectionTypeOptions? libraryCollectionType) where T : BaseItem
    {
        if (!libraryCollectionType.HasValue)
            return false;

        // Check if the collection type is compatible with the target item type
        return typeof(T) switch
        {
            Type t when t == typeof(Movie) => libraryCollectionType.Value == CollectionTypeOptions.movies || libraryCollectionType.Value == CollectionTypeOptions.mixed,
            Type t when t == typeof(Series) => libraryCollectionType.Value == CollectionTypeOptions.tvshows || libraryCollectionType.Value == CollectionTypeOptions.mixed,
            _ => false // Unsupported item type
        };
    }

    public static BaseItemKind GetBaseItemKind<T>() where T : BaseItem
    {
        return typeof(T) switch
        {
            Type t when t == typeof(Movie) => MovieKind,
            Type t when t == typeof(Series) => SeriesKind,
            _ => throw new NotSupportedException($"Unsupported item type: {typeof(T).Name}")
        };
    }

}
