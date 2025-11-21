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
    /// <param name="libraryPath">Optional library path to filter by</param>
    /// <returns>List of existing items</returns>
    public List<T> GetExistingItems<T>(string? libraryPath = null) where T : class, IJellyfinItem
    {
        try
        {
            // Get the appropriate BaseItemKind for the type
            if (typeof(T) == typeof(JellyfinMovie))
            {
                var itemTypes = new[] { BaseItemKind.Movie };
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
                var itemTypes = new[] { BaseItemKind.Series };
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
    /// Get user's library items of a specific type (excluding favorites filter).
    /// </summary>
    /// <typeparam name="T">The type of Jellyfin wrapper to retrieve (JellyfinMovie, JellyfinSeries)</typeparam>
    /// <param name="user">The user to get library items for</param>
    /// <param name="excludePaths">Optional set of paths to exclude from results</param>
    /// <returns>List of user's library items</returns>
    public List<T> GetUserLibraryItems<T>(JellyfinUser user, HashSet<string>? excludePaths = null) where T : class, IJellyfinItem
    {
        try
        {
            var result = new List<T>();
            
            if (typeof(T) == typeof(JellyfinMovie))
            {
                var items = Inner.GetItemList(new InternalItemsQuery(user.Inner)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    Recursive = true
                });
                
                var jellyfinItems = items.Select<BaseItem, T?>(item => 
                {
                    if (item is Movie movie)
                    {
                        return (T)(object)JellyfinMovie.FromMovie(movie);
                    }
                    return null;
                }).Where(item => item != null && 
                    (excludePaths == null || item.Path == null || 
                     !excludePaths.Any(excludePath => FolderUtils.IsPathInDirectory(item.Path, excludePath)))).Cast<T>();
                
                result.AddRange(jellyfinItems);
            }
            else if (typeof(T) == typeof(JellyfinSeries))
            {
                var items = Inner.GetItemList(new InternalItemsQuery(user.Inner)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    Recursive = true
                });
                
                var jellyfinItems = items.Select<BaseItem, T?>(item => 
                {
                    if (item is Series series)
                    {
                        return (T)(object)JellyfinSeries.FromSeries(series);
                    }
                    return null;
                }).Where(item => item != null && 
                    (excludePaths == null || item.Path == null || 
                     !excludePaths.Any(excludePath => FolderUtils.IsPathInDirectory(item.Path, excludePath)))).Cast<T>();
                
                result.AddRange(jellyfinItems);
            }
            
            return result;
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

        // Try FindByPath as non-folder (for movies)
        item = Inner.FindByPath(directoryPath, isFolder: false);
        if (item != null)
        {
            return item;
        }

        // For movies, the path might be stored as the video file path, not the directory
        // Try to find video files in the directory and search by those paths
        if (Directory.Exists(directoryPath))
        {
            // Common video file extensions
            var videoExtensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".flv", ".webm", ".mpg", ".mpeg", ".m2ts", ".ts", ".mts" };
            
            try
            {
                var files = Directory.GetFiles(directoryPath);
                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (videoExtensions.Contains(extension))
                    {
                        // Try to find the item by the video file path
                        item = Inner.FindByPath(file, isFolder: false);
                        if (item != null)
                        {
                            return item;
                        }
                    }
                }
            }
            catch
            {
                // If we can't read the directory, ignore and continue
            }
        }

        // If FindByPath doesn't work, return null
        // We avoid using GetItemList as a fallback because it can fail with deserialization errors
        // when some items in the library have unknown types
        return null;
    }

}