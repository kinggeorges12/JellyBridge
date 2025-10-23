using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Controllers;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyseerrBridge.Tasks;

/// <summary>
/// Scheduled task for syncing Jellyseerr data.
/// </summary>
public class JellyseerrSyncTask : IScheduledTask
{
    private readonly ILogger<JellyseerrSyncTask> _logger;
    private readonly RouteController _routeController;


    public JellyseerrSyncTask(
        ILogger<JellyseerrSyncTask> logger,
        RouteController routeController)
    {
        _logger = logger;
        _routeController = routeController;
    }

    public string Name => "Jellyseerr Bridge Sync";
    public string Key => "JellyseerrBridgeSync";
    public string Description => "Syncs data from Jellyseerr to Jellyfin libraries";
    public string Category => "Jellyseerr Bridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting scheduled Jellyseerr sync task");
            
            progress.Report(0);
            
            // Call the SyncLibrary method directly
            var result = await _routeController.SyncLibrary();
            
            progress.Report(100);
            
            if (result is Microsoft.AspNetCore.Mvc.OkObjectResult okResult)
            {
                _logger.LogInformation("Scheduled Jellyseerr sync task completed successfully");
            }
            else
            {
                _logger.LogWarning("Scheduled Jellyseerr sync task failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in scheduled Jellyseerr sync task");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = Plugin.GetConfiguration();
        var isEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
        
        if (!isEnabled)
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours))).Ticks
            }
        };
    }
}

