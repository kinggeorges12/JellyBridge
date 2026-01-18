using Jellyfin.Plugin.JellyBridge.Controllers;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// <see cref="IHostedService"/> responsible for handling favorite item events.
/// Subscribes to UserDataSaved events to detect when items are favorited.
/// </summary>
public sealed class FavoriteEventHandler : IHostedService
{
    private readonly ILogger<FavoriteEventHandler> _logger;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinIUserManager _userManager;
    private readonly ManageDiscoverLibraryController _manageDiscoverLibraryController;

    /// <summary>
    /// Initializes a new instance of the <see cref="FavoriteEventHandler"/> class.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/>.</param>
    /// <param name="userDataManager">The <see cref="JellyfinIUserDataManager"/>.</param>
    /// <param name="userManager">The <see cref="JellyfinIUserManager"/>.</param>
    public FavoriteEventHandler(
        ILogger<FavoriteEventHandler> logger,
        JellyfinIUserDataManager userDataManager,
        JellyfinIUserManager userManager,
        ManageDiscoverLibraryController manageDiscoverLibraryController)
    {
        _logger = new DebugLogger<FavoriteEventHandler>(logger);
        _userDataManager = userDataManager;
        _userManager = userManager;
        _manageDiscoverLibraryController = manageDiscoverLibraryController;
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

    /// <summary>
    /// Handles the UserDataSaved event to detect favorite additions.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Only process UpdateUserRating events (used when favorites are added/removed)
        if (e.SaveReason != UserDataSaveReason.UpdateUserRating)
        {
            return;
        }

        // Check if this is a favorite change
        if (e.UserData == null || e.Item == null || !e.UserData.IsFavorite)
        {
            return;
        }

        try
        {
            var user = _userManager.GetUserById(e.UserId);
            if (user == null)
            {
                return;
            }

            // Check ResponsiveFavoriteRequests flag using Plugin.GetConfigOrDefault
            if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ResponsiveFavoriteRequests)))
            {
                _logger.LogDebug("Responsive Favorite Requests is disabled. Skipping favorites sync to Jellyseerr.");
                return;
            }

            // Check if item is in a JellyBridge sync directory using FolderUtils.IsPathInSyncDirectory
            var itemPath = e.Item.Path;
            if (!FolderUtils.IsPathInSyncDirectory(itemPath))
            {
                _logger.LogDebug("Media item is not in a JellyBridge sync directory. Skipping favorites sync to Jellyseerr. ItemPath={ItemPath}", itemPath);
                return;
            }

            _logger.LogInformation("Favorite added: User={UserName}, Item={ItemName} (Id={ItemId})", 
                user.Username, e.Item.Name, e.Item.Id);

            // Fire and forget, log errors
            _ = TriggerSyncFavoritesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling favorite event for ItemId={ItemId}, UserId={UserId}", 
                e.Item?.Id, e.UserId);
        }

    async Task TriggerSyncFavoritesAsync()
    {
        try
        {
            var result = await _manageDiscoverLibraryController.SyncFavorites();
            _logger.LogInformation("Triggered SyncFavorites from FavoriteEventHandler. Result: {Result}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running SyncFavorites from FavoriteEventHandler");
        }
    }
    }
}

