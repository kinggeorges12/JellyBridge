using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Startup task for syncing Jellyseerr data with delay.
/// </summary>
public class StartupTask : IScheduledTask
{
    private readonly ILogger<StartupTask> _logger;
    private readonly SyncService _syncService;
    private readonly ITaskManager _taskManager;

    public StartupTask(
        ILogger<StartupTask> logger,
        SyncService syncService,
        ITaskManager taskManager)
    {
        _logger = logger;
        _syncService = syncService;
        _taskManager = taskManager;
    }

    public string Name => "JellyBridge Startup";
    public string Key => "JellyBridgeStartup";
    public string Description => "Syncs favorites to Jellyseerr and discovers content from Jellyseerr to Jellyfin (Startup)";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting startup sync task");
            
            var autoSyncOnStartup = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AutoSyncOnStartup));
            var startupDelaySeconds = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.StartupDelaySeconds));
            
            if (autoSyncOnStartup && startupDelaySeconds > 0)
            {
                _logger.LogInformation("[StartupTask] Applying startup delay of {StartupDelaySeconds} seconds", startupDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), cancellationToken);
            }
            
            // Get the SyncTask and execute it
            var syncTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSync");
            if (syncTask?.ScheduledTask is SyncTask task)
            {
                await task.ExecuteAsync(progress, cancellationToken);
            }
            else
            {
                _logger.LogWarning("[StartupTask] Could not find SyncTask to execute");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in startup sync task");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var isEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
        var autoSyncOnStartup = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AutoSyncOnStartup));
        
        if (!isEnabled || !autoSyncOnStartup)
        {
            _logger.LogDebug("[StartupTask] Plugin disabled or startup sync disabled, returning empty triggers");
            return Array.Empty<TaskTriggerInfo>();
        }

        _logger.LogDebug("[StartupTask] Added startup trigger");
        
        return new List<TaskTriggerInfo>
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerStartup
            }
        };
    }
}

