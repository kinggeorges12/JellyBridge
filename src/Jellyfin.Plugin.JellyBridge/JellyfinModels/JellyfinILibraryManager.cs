using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's ILibraryManager interface.
/// Version-specific implementation with conditional compilation for namespace changes.
/// </summary>
public class JellyfinILibraryManager : WrapperBase<ILibraryManager>
{
    public JellyfinILibraryManager(ILibraryManager libraryManager) : base(libraryManager) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Get existing items of a specific type from the library.
    /// </summary>
    /// <typeparam name="T">The type of Jellyfin wrapper to retrieve (JellyfinMovie, JellyfinSeries)</typeparam>
    /// <param name="libraryPath">Optional library path to filter by</param>
    /// <returns>List of existing items</returns>
    public List<T> GetExistingItems<T>(string? libraryPath = null) where T : class, IJellyfinItem
    {
        try
        {
            // Get the appropriate BaseItemKind for the type
            if (typeof(T) == typeof(JellyfinMovie))
            {
                var itemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie };
                var items = Inner.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = itemTypes,
                    Recursive = true
                });
                
                // Convert BaseItem results to Jellyfin wrapper using factory methods
                var jellyfinItems = items.Select<BaseItem, T?>(item => 
                {
                    if (item is Movie movie)
                    {
                        return (T)(object)JellyfinMovie.FromMovie(movie);
                    }
                    return null;
                }).Where(item => item != null).Cast<T>();
                
                var filteredItems = jellyfinItems;
                
                // Filter by library path if provided
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    filteredItems = filteredItems.Where(item => 
                        !string.IsNullOrEmpty(item.Path) && 
                        item.Path.StartsWith(libraryPath, StringComparison.OrdinalIgnoreCase));
                }
                
                return filteredItems.ToList();
            }
            else if (typeof(T) == typeof(JellyfinSeries))
            {
                var itemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Series };
                var items = Inner.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = itemTypes,
                    Recursive = true
                });
                
                // Convert BaseItem results to Jellyfin wrapper using factory methods
                var jellyfinItems = items.Select<BaseItem, T?>(item => 
                {
                    if (item is Series series)
                    {
                        return (T)(object)JellyfinSeries.FromSeries(series);
                    }
                    return null;
                }).Where(item => item != null).Cast<T>();
                
                var filteredItems = jellyfinItems;
                
                // Filter by library path if provided
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    filteredItems = filteredItems.Where(item => 
                        !string.IsNullOrEmpty(item.Path) && 
                        item.Path.StartsWith(libraryPath, StringComparison.OrdinalIgnoreCase));
                }
                
                return filteredItems.ToList();
            }
            else
            {
                return new List<T>();
            }
        }
        catch (MissingMethodException)
        {
            // Using incompatible Jellyfin version
        }
        catch (Exception)
        {
            // Error getting existing items
        }
        return new List<T>();
    }

    /// <summary>
    /// Finds an item by its directory path. Tries FindByPath as both folder and non-folder.
    /// </summary>
    /// <param name="directoryPath">The directory path to search for</param>
    /// <returns>The BaseItem if found, null otherwise</returns>
    public BaseItem? FindItemByDirectoryPath(string directoryPath)
    {
        // First try FindByPath as folder (for shows)
        var item = Inner.FindByPath(directoryPath, isFolder: true);
        if (item != null)
        {
            return item;
        }

        // Try FindByPath as non-folder (for movies)
        item = Inner.FindByPath(directoryPath, isFolder: false);
        if (item != null)
        {
            return item;
        }

        // If FindByPath doesn't work, return null
        // We avoid using GetItemList as a fallback because it can fail with deserialization errors
        // when some items in the library have unknown types
        return null;
    }

}