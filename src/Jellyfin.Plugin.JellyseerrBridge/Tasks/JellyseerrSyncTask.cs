using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyseerrBridge.Tasks;

/// <summary>
/// Scheduled task for syncing Jellyseerr data.
/// </summary>
public class JellyseerrSyncTask : IScheduledTask
{
    private readonly ILogger<JellyseerrSyncTask> _logger;
    private readonly JellyseerrSyncService _syncService;

    public JellyseerrSyncTask(
        ILogger<JellyseerrSyncTask> logger,
        JellyseerrSyncService syncService)
    {
        _logger = logger;
        _syncService = syncService;
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
            
            await _syncService.SyncAsync();
            
            progress.Report(100);
            
            _logger.LogInformation("Scheduled Jellyseerr sync task completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in scheduled Jellyseerr sync task");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = Plugin.Instance.Configuration;
        
        if (!(config.IsEnabled ?? (bool)PluginConfiguration.DefaultValues[nameof(config.IsEnabled)]))
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(config.SyncIntervalHours).Ticks
            }
        };
    }
}
