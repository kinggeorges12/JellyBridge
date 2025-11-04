using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System.Threading;
using System;

#if JELLYFIN_10_11
// Jellyfin version 10.11.*
using JellyfinUserEntity = Jellyfin.Database.Implementations.Entities.User;
#else
// Jellyfin version 10.10.*
using JellyfinUserEntity = Jellyfin.Data.Entities.User;
#endif

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's IUserDataManager interface.
/// Version-specific implementation with conditional compilation for User type namespace changes.
/// </summary>
public class JellyfinIUserDataManager : WrapperBase<IUserDataManager>
{
    public JellyfinIUserDataManager(IUserDataManager userDataManager) : base(userDataManager) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Get all favorites for all users from Jellyfin.
    /// </summary>
    /// <typeparam name="T">The type of Jellyfin wrapper items to return (JellyfinMovie, JellyfinSeries, IJellyfinItem)</typeparam>
    /// <param name="userManager">The user manager</param>
    /// <param name="libraryManager">The library manager wrapper</param>
    /// <returns>Dictionary mapping users to their favorite items</returns>
    public Dictionary<JellyfinUser, List<T>> GetUserFavorites<T>(JellyfinIUserManager userManager, JellyfinILibraryManager libraryManager) where T : class
    {
        var userFavorites = new Dictionary<JellyfinUser, List<T>>();
        
        // Get all users from user manager wrapper
        var users = userManager.GetAllUsers().ToList();
        
        // Get favorites for each user directly
        foreach (var user in users)
        {
            var userFavs = libraryManager.Inner.GetItemList(new InternalItemsQuery(user.Inner)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                IsFavorite = true,
                Recursive = true
            });
            
            // Convert to the requested Jellyfin wrapper type
            var convertedFavs = userFavs.Select<BaseItem, T?>(item => 
            {
                if (typeof(T) == typeof(JellyfinMovie) && item is Movie movie)
                {
                    return (T)(object)JellyfinMovie.FromMovie(movie);
                }
                else if (typeof(T) == typeof(JellyfinSeries) && item is Series series)
                {
                    return (T)(object)JellyfinSeries.FromSeries(series);
                }
                else if (typeof(T) == typeof(IJellyfinItem))
                {
                    if (item is Movie movieForBase)
                        return (T)(object)JellyfinMovie.FromMovie(movieForBase);
                    else if (item is Series seriesForBase)
                        return (T)(object)JellyfinSeries.FromSeries(seriesForBase);
                }
                
                return null;
            }).Where(item => item != null).Cast<T>().ToList();
            
            // user is already a JellyfinUser wrapper
            userFavorites[user] = convertedFavs;
        }
        
        return userFavorites;
    }

    /// <summary>
    /// Set or unset the favorite flag for the given user and item using wrappers.
    /// GetUserData automatically creates user data if it doesn't exist in both 10.10 and 10.11.
    /// </summary>
    public bool TrySetFavorite(JellyfinUser user, IJellyfinItem item, bool isFavorite, JellyfinILibraryManager libraryManager)
    {
        var userEntity = user.Inner;
        var baseItem = libraryManager.Inner.GetItemById<BaseItem>(item.Id, userEntity);
        if (baseItem is null)
        {
            return false;
        }

#if JELLYFIN_10_11
        // Jellyfin 10.11: GetUserData returns UserItemData? (nullable in signature) but implementation always creates/returns a value
        var data = Inner.GetUserData(userEntity, baseItem);
        if (data is null)
        {
            // Should never happen per implementation, but handle nullable signature defensively
            return false;
        }
#else
        // Jellyfin 10.10: GetUserData returns UserItemData (non-nullable) - always creates/returns a value
        var data = Inner.GetUserData(userEntity, baseItem);
#endif
        
        // GetUserData automatically creates user data if it doesn't exist, so we just set the favorite flag
        data.IsFavorite = isFavorite;
        Inner.SaveUserData(userEntity, baseItem, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Gets user data for a user and item. Returns null if no user data exists.
    /// </summary>
    public UserItemData? GetUserData(JellyfinUser user, BaseItem item)
    {
        var userEntity = user.Inner;
        
#if JELLYFIN_10_11
        // Jellyfin 10.11: GetUserData returns UserItemData? (nullable)
        return Inner.GetUserData(userEntity, item);
#else
        // Jellyfin 10.10: GetUserData returns UserItemData (non-nullable) but might return new instance
        var data = Inner.GetUserData(userEntity, item);
        // Check if it's actually new/empty by checking if it has been saved (has a Key)
        // If Key is empty or null, this is likely a new instance that was just created
        if (data != null && string.IsNullOrEmpty(data.Key))
        {
            // This is a new instance, return null to indicate no existing data
            return null;
        }
        return data;
#endif
    }

    /// <summary>
    /// Updates play count and last played date for a user and item asynchronously. GetUserData automatically creates user data if it doesn't exist.
    /// Does not modify the Played flag.
    /// </summary>
    /// <param name="user">The user to update play count for</param>
    /// <param name="item">The item to update</param>
    /// <param name="playCount">The play count to set</param>
    /// <param name="assignedPlayDate">The date to set for LastPlayedDate. If null, uses DateTime.UtcNow</param>
    public async Task<bool> TryUpdatePlayCountAsync(JellyfinUser user, IJellyfinItem item, int playCount, DateTime? assignedPlayDate = null)
    {
        var userEntity = user.Inner;
        
        // Get the BaseItem from the wrapper
        BaseItem baseItem = item switch
        {
            JellyfinMovie movie => movie,
            JellyfinSeries series => series,
            _ => throw new ArgumentException($"Unsupported item type: {item.GetType().Name}", nameof(item))
        };
        
#if JELLYFIN_10_11
        // Jellyfin 10.11: GetUserData returns UserItemData? (nullable in signature) but implementation always creates/returns a value
        var userData = Inner.GetUserData(userEntity, baseItem);
        if (userData is null)
        {
            // Should never happen per implementation, but handle nullable signature defensively
            return false;
        }
#else
        // Jellyfin 10.10: GetUserData returns UserItemData (non-nullable) - always creates/returns a value
        var userData = Inner.GetUserData(userEntity, baseItem);
#endif
        
        // GetUserData automatically creates user data if it doesn't exist, so we just set the play count and last played date
        userData.PlayCount = playCount;
        // Set LastPlayedDate to assigned date (null allowed for zero play count)
        userData.LastPlayedDate = assignedPlayDate;
        // Do not modify the Played flag - it remains in its current state
        // SaveUserData is synchronous, so we wrap it in Task.Run to make it truly asynchronous
        await Task.Run(() => Inner.SaveUserData(userEntity, baseItem, userData, UserDataSaveReason.Import, CancellationToken.None)).ConfigureAwait(false);
        return true;
    }


}