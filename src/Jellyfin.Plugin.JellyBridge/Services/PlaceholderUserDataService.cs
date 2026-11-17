using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Resets placeholder user data on every Discover sync so placeholders stay out of Continue Watching and Next Up.
/// </summary>
public class PlaceholderUserDataService
{
    private readonly DebugLogger<PlaceholderUserDataService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinIUserManager _userManager;

    public PlaceholderUserDataService(
        ILogger<PlaceholderUserDataService> logger,
        JellyfinILibraryManager libraryManager,
        JellyfinIUserDataManager userDataManager,
        JellyfinIUserManager userManager)
    {
        _logger = new DebugLogger<PlaceholderUserDataService>(logger);
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
    }

    /// <summary>
    /// True for a per-season placeholder episode ("Season NN/SNNE9999.mp4", NN &gt; 0).
    /// </summary>
    public static bool IsSeasonPlaceholder(BaseItem item)
    {
        if (item is not Episode episode)
        {
            return false;
        }
        var seasonNumber = episode.ParentIndexNumber
            ?? PlaceholderVideoGenerator.ParseSeasonFolderNumber(System.IO.Path.GetDirectoryName(item.Path));
        return seasonNumber.HasValue && seasonNumber.Value > 0;
    }

    /// <summary>
    /// Reset resume/played/last-played state of all placeholder videos in the JellyBridge folder for all users.
    /// </summary>
    /// <returns>Number of user-data records changed.</returns>
    public int ResetPlaceholderPlaybackState()
    {
        var changedCount = 0;
        try
        {
            // Every JellyBridge library folder (all Discover libraries), each inside its own Jellyfin library
            var rootIds = FolderUtils.GetBridgeDirectories()
                .Select(directory => _libraryManager.Inner.FindByPath(directory, isFolder: true)?.Id ?? Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
            if (rootIds.Length == 0)
            {
                _logger.LogDebug("JellyBridge folders are not in Jellyfin libraries yet; skipping placeholder user data cleanup");
                return 0;
            }

            foreach (var user in _userManager.GetAllUsers())
            {
                var items = _libraryManager.Inner.GetItemList(new InternalItemsQuery(user.Inner)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
                    AncestorIds = rootIds,
                    Recursive = true
                });

                foreach (var item in items.OfType<Video>())
                {
                    if (string.IsNullOrEmpty(item.Path) || !FolderUtils.IsPathInAnyBridgeDirectory(item.Path))
                    {
                        continue;
                    }
                    var data = _userDataManager.GetUserData(user, item);
                    if (data == null)
                    {
                        continue;
                    }

                    var isEpisode = item is Episode;
                    var changed = false;
                    if (data.PlaybackPositionTicks != 0)
                    {
                        data.PlaybackPositionTicks = 0;
                        changed = true;
                    }
                    if (data.Played && IsSeasonPlaceholder(item))
                    {
                        data.Played = false;
                        changed = true;
                    }
                    if (isEpisode && data.LastPlayedDate.HasValue)
                    {
                        data.LastPlayedDate = null;
                        changed = true;
                    }
                    if (changed)
                    {
                        _userDataManager.Inner.SaveUserData(user.Inner, item, data, UserDataSaveReason.Import, CancellationToken.None);
                        changedCount++;
                    }
                }
            }

            if (changedCount > 0)
            {
                _logger.LogDebug("Reset playback state of {Count} placeholder user data records", changedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Placeholder user data cleanup failed");
        }
        return changedCount;
    }
}
