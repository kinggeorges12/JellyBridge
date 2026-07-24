using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Jellyfin.Plugin.JellyBridge.Utils;
using System;
using System.IO;
using System.Linq;

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
    /// <param name="includePaths">Optional set of paths to include in the results</param>
    /// <param name="excludePaths">Optional set of paths to exclude from the results</param>
    /// <returns>List of existing items</returns>
    public List<T> GetExistingItems<T>(HashSet<string>? includePaths = null, HashSet<string>? excludePaths = null) where T : class, IJellyfinItem
    {
        return GetUserLibraryItems<T>(includePaths: includePaths, excludePaths: excludePaths);
    }

    /// <summary>
    /// Get user's library items of a specific type (excluding favorites filter).
    /// </summary>
    /// <typeparam name="T">The type of Jellyfin wrapper to retrieve (JellyfinMovie, JellyfinSeries)</typeparam>
    /// <param name="user">The user to get library items for</param>
    /// <param name="includePaths">Optional set of paths to include in results</param>
    /// <param name="excludePaths">Optional set of paths to exclude from results</param>
    /// <returns>List of user's library items</returns>
    public List<T> GetUserLibraryItems<T>(JellyfinUser? user = null, HashSet<string>? includePaths = null, HashSet<string>? excludePaths = null) where T : class, IJellyfinItem
    {
        try
        {
            IEnumerable<T>? jellyfinItems = null;
            IReadOnlyList<BaseItem> items;

            // Determine the enum for creating the search query
            BaseItemKind baseItemKind;
            if (typeof(T) == typeof(JellyfinSeries)){
                baseItemKind = BaseItemKind.Series;
            }
            else if (typeof(T) == typeof(JellyfinMovie)){
                baseItemKind = BaseItemKind.Movie;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported type {typeof(T).Name}");
            }

            // Search the whole library if no user is provided, otherwise search the user's library
            if (user == null)
            {
                items = Inner.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { baseItemKind },
                    Recursive = true
                });
            }
            else
            {
                items = Inner.GetItemList(new InternalItemsQuery(user.Inner)
                {
                    IncludeItemTypes = new[] { baseItemKind },
                    Recursive = true
                });
            }
            
            // Convert the base items to the appropriate Jellyfin wrapper type
            jellyfinItems = items.Select<BaseItem, T?>(item => 
            {
                if (item is Movie movie)
                {
                    return (T)(object)JellyfinMovie.FromMovie(movie);
                }
                else if (item is Series series)
                {
                    return (T)(object)JellyfinSeries.FromSeries(series);
                }
                return null;
            }).Cast<T>();
            
            if (jellyfinItems == null)
            {
                return new List<T>();
            }
            // Filter by library path if provided
            var filteredItems = jellyfinItems.Where(item =>
                item != null && 
                !string.IsNullOrEmpty(item.Path) &&
                (includePaths == null || 
                includePaths.Any(includePath => FolderUtils.IsPathInDirectory(item.Path, includePath))) &&
                (excludePaths == null || 
                !excludePaths.Any(excludePath => FolderUtils.IsPathInDirectory(item.Path, excludePath)))
            );
            
            return filteredItems.ToList();
        }
        catch (MissingMethodException)
        {
            // Using incompatible Jellyfin version
        }
        catch (Exception)
        {
            // Error getting user library items
        }
        return new List<T>();
    }

    /// <summary>
    /// Finds an item by its directory path. Tries FindByPath as both folder and non-folder.
    /// For movies, also searches for video files in the directory and finds items by those file paths.
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

        // Jellyfin >v10.11.3 do not allow movies by 'movie.mp4' path
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