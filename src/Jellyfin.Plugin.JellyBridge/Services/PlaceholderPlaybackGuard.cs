using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Keeps placeholder videos out of Continue Watching and Next Up by resetting their user data after playback.
/// Only active with Season Placeholders on.
/// </summary>
public sealed class PlaceholderPlaybackGuard : IHostedService
{
    private readonly DebugLogger<PlaceholderPlaybackGuard> _logger;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinIUserManager _userManager;

    public PlaceholderPlaybackGuard(
        ILogger<PlaceholderPlaybackGuard> logger,
        JellyfinIUserDataManager userDataManager,
        JellyfinIUserManager userManager)
    {
        _logger = new DebugLogger<PlaceholderPlaybackGuard>(logger);
        _userDataManager = userDataManager;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private static bool IsInSyncDirectory(string path)
    {
        try
        {
            return FolderUtils.IsPathInAnyBridgeDirectory(path);
        }
        catch (Exception)
        {
            return false; // library directory not configured or missing
        }
    }

    private static bool IsSeasonPlaceholder(Video video) => PlaceholderUserDataService.IsSeasonPlaceholder(video);

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        try
        {
            if (e.Item is not Video video || e.UserData == null)
            {
                return;
            }

            var reason = e.SaveReason;
            var isPlayback = reason == UserDataSaveReason.PlaybackStart
                || reason == UserDataSaveReason.PlaybackProgress
                || reason == UserDataSaveReason.PlaybackFinished;
            var isManualPlayed = reason == UserDataSaveReason.TogglePlayed
                || reason == UserDataSaveReason.UpdateUserData;
            if (!isPlayback && !isManualPlayed)
            {
                return; // Import (our own reset, SortService) and rating changes are left alone
            }

            if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseSeasonPlaceholders)))
            {
                return;
            }

            var isEpisode = video is Episode;

            // Cheap check first: nothing to reset
            if (e.UserData.PlaybackPositionTicks == 0 && !e.UserData.Played && !(isEpisode && e.UserData.LastPlayedDate.HasValue))
            {
                return;
            }

            var path = video.Path;
            if (string.IsNullOrEmpty(path) || !IsInSyncDirectory(path))
            {
                return;
            }

            var seasonPlaceholder = IsSeasonPlaceholder(video);
            if (isManualPlayed && !isEpisode)
            {
                return; // manual mark-played of movie placeholders is respected
            }

            // Target Played state: season placeholders never played; after playback others follow MarkMediaPlayed
            // (false = unplayed); a manual mark-played of the specials promo keeps its Played flag.
            bool? targetPlayed = seasonPlaceholder
                || (isPlayback && !Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.MarkMediaPlayed)))
                ? false
                : null;

            var userId = e.UserId;
            var itemName = video.Name;
            // Run after the caller (session manager) has finished its own save
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    var user = _userManager.GetUserById(userId);
                    if (user == null)
                    {
                        return;
                    }
                    var data = _userDataManager.GetUserData(user, video);
                    if (data == null)
                    {
                        return;
                    }

                    var changed = false;
                    if (data.PlaybackPositionTicks != 0)
                    {
                        data.PlaybackPositionTicks = 0;
                        changed = true;
                    }
                    if (targetPlayed.HasValue && data.Played != targetPlayed.Value)
                    {
                        data.Played = targetPlayed.Value;
                        changed = true;
                    }
                    if (isEpisode && data.LastPlayedDate.HasValue)
                    {
                        data.LastPlayedDate = null; // keeps the Discover show out of Next Up
                        changed = true;
                    }
                    if (!changed)
                    {
                        return;
                    }

                    _userDataManager.Inner.SaveUserData(user.Inner, video, data, UserDataSaveReason.Import, CancellationToken.None);
                    _logger.LogDebug("Reset placeholder '{ItemName}' for user {UserName} after {Reason}: position 0, played {Played}, play count {PlayCount}",
                        itemName, user.Username, reason, data.Played, data.PlayCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reset placeholder playback state for '{ItemName}'", itemName);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling user data event for placeholder {ItemId}", e.Item?.Id);
        }
    }
}
