using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System.Threading;

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
    public Dictionary<JellyfinUser, List<T>> GetUserFavorites<T>(IUserManager userManager, JellyfinILibraryManager libraryManager) where T : class
    {
        var userFavorites = new Dictionary<JellyfinUser, List<T>>();
        
        // Get all users from user manager
        var users = userManager.Users.ToList();
        
        // Get favorites for each user directly
        foreach (var user in users)
        {
            var userFavs = libraryManager.Inner.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie, Jellyfin.Data.Enums.BaseItemKind.Series },
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
            
            // Convert user to JellyfinUser wrapper
            var jellyfinUser = new JellyfinUser((dynamic)user);
            userFavorites[jellyfinUser] = convertedFavs;
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
    /// Updates play count for a user and item. GetUserData automatically creates user data if it doesn't exist.
    /// </summary>
    public bool TryUpdatePlayCount(JellyfinUser user, BaseItem item, int playCount)
    {
        var userEntity = user.Inner;
        
#if JELLYFIN_10_11
        // Jellyfin 10.11: GetUserData returns UserItemData? (nullable in signature) but implementation always creates/returns a value
        var userData = Inner.GetUserData(userEntity, item);
        if (userData is null)
        {
            // Should never happen per implementation, but handle nullable signature defensively
            return false;
        }
#else
        // Jellyfin 10.10: GetUserData returns UserItemData (non-nullable) - always creates/returns a value
        var userData = Inner.GetUserData(userEntity, item);
#endif
        
        // GetUserData automatically creates user data if it doesn't exist, so we just set the play count
        userData.PlayCount = playCount;
        Inner.SaveUserData(userEntity, item, userData, UserDataSaveReason.Import, CancellationToken.None);
        return true;
    }

}