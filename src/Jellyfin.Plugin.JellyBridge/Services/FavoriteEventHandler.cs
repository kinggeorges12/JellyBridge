using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="FavoriteEventHandler"/> class.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/>.</param>
    /// <param name="userDataManager">The <see cref="JellyfinIUserDataManager"/>.</param>
    /// <param name="userManager">The <see cref="JellyfinIUserManager"/>.</param>
    public FavoriteEventHandler(
        ILogger<FavoriteEventHandler> logger,
        JellyfinIUserDataManager userDataManager,
        JellyfinIUserManager userManager)
    {
        _logger = new DebugLogger<FavoriteEventHandler>(logger);
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

            _logger.LogInformation("Favorite added: User={UserName}, Item={ItemName} (Id={ItemId})", 
                user.Username, e.Item.Name, e.Item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling favorite event for ItemId={ItemId}, UserId={UserId}", 
                e.Item?.Id, e.UserId);
        }
    }
}

