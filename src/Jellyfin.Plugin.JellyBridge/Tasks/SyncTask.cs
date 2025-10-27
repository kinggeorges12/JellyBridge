using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Controllers;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Scheduled task for syncing Jellyseerr data.
/// </summary>
public class SyncTask : IScheduledTask
{
    private readonly ILogger<SyncTask> _logger;
    private readonly SyncService _syncService;
    private readonly ITaskManager _taskManager;


    public SyncTask(
        ILogger<SyncTask> logger,
        SyncService syncService,
        ITaskManager taskManager)
    {
        _logger = logger;
        _syncService = syncService;
        _taskManager = taskManager;
        _logger.LogInformation("SyncTask constructor called - task initialized");
    }

    public string Name => "JellyBridge Sync";
    public string Key => "JellyBridgeSync";
    public string Description => "Syncs favorites to Jellyseerr and discovers content from Jellyseerr to Jellyfin";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting scheduled sync task");
            
            var autoSyncOnStartup = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AutoSyncOnStartup));
            var startupDelaySeconds = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.StartupDelaySeconds));
            
            // Determine if this is the first run by checking if the task has ever completed before
            bool isFirstRun = false;
            var task = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == Key);
            if (task?.LastExecutionResult == null || task.LastExecutionResult.StartTimeUtc == DateTime.MinValue)
            {
                isFirstRun = true;
                _logger.LogInformation("[SyncTask] This appears to be the first run - no previous execution result found");
            }
            
            _logger.LogInformation("[SyncTask] IsFirstRun: {IsFirstRun}, AutoSyncOnStartup: {AutoSyncOnStartup}, StartupDelaySeconds: {StartupDelaySeconds}", 
                isFirstRun, autoSyncOnStartup, startupDelaySeconds);
            
            // Only apply startup delay on first run and if auto-sync on startup is enabled
            if (isFirstRun && autoSyncOnStartup && startupDelaySeconds > 0)
            {
                _logger.LogInformation("[SyncTask] First run detected with startup sync enabled - waiting {StartupDelaySeconds} seconds before sync", startupDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), cancellationToken);
            }
            
            // Use Jellyfin-style locking that pauses instead of canceling
            await Plugin.ExecuteWithLockAsync<(SyncJellyfinResult?, SyncJellyseerrResult?)>(async () =>
            {
                SyncJellyfinResult? syncToResult = null;
                SyncJellyseerrResult? syncFromResult = null;
                
                // Step 1: Sync favorites to Jellyseerr (0-50%)
                progress.Report(0);
                _logger.LogDebug("Step 1: Syncing favorites to Jellyseerr...");
                
                try
                {
                    syncToResult = await _syncService.SyncToJellyseerr();
                    progress.Report(50);
                    _logger.LogDebug("Step 1 completed: {Success} - {Message}", syncToResult.Success, syncToResult.Message);
                    _logger.LogDebug("Step 1 details: {Details}", syncToResult.Details);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Step 1 failed: Sync to Jellyseerr");
                    syncToResult = new SyncJellyfinResult
                    {
                        Success = false,
                        Message = $"❌ Sync to Jellyseerr failed: {ex.Message}",
                        Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}"
                    };
                    progress.Report(50);
                }
                
                // Step 2: Sync discover from Jellyseerr (50-100%)
                _logger.LogDebug("Step 2: Syncing discover from Jellyseerr...");
                
                try
                {
                    syncFromResult = await _syncService.SyncFromJellyseerr();
                    progress.Report(100);
                    _logger.LogDebug("Step 2 completed: {Success} - {Message}", syncFromResult.Success, syncFromResult.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Step 2 failed: Sync from Jellyseerr");
                    syncFromResult = new SyncJellyseerrResult
                    {
                        Success = false,
                        Message = $"❌ Sync from Jellyseerr failed: {ex.Message}",
                        Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}"
                    };
                    progress.Report(100);
                }
                
                _logger.LogDebug("Sync details - To Jellyseerr: {ToDetails}, From Jellyseerr: {FromDetails}", 
                    syncToResult?.Details, syncFromResult?.Details);
                
                _logger.LogInformation("Scheduled Jellyseerr sync task completed - To Jellyseerr: {ToSuccess}, From Jellyseerr: {FromSuccess}", 
                    syncToResult?.Success, syncFromResult?.Success);
                
                return (syncToResult, syncFromResult);
            }, _logger, "Scheduled Sync");
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
        var autoSyncOnStartup = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AutoSyncOnStartup));
        var intervalHours = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours));
        var startupDelaySeconds = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.StartupDelaySeconds));
        
        _logger.LogInformation("Adding default triggers - IsEnabled: {IsEnabled}, IntervalHours: {IntervalHours}, AutoSyncOnStartup: {AutoSyncOnStartup}, StartupDelaySeconds: {StartupDelaySeconds}",
            isEnabled, autoSyncOnStartup, intervalHours, startupDelaySeconds);
        
        if (!isEnabled)
        {
            _logger.LogDebug("[SyncTask] Plugin is disabled, returning empty triggers");
            return Array.Empty<TaskTriggerInfo>();
        }

        var triggers = new List<TaskTriggerInfo>();
        
        // Add interval trigger for regular syncing
        triggers.Add(new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
        });
        
        _logger.LogDebug("[SyncTask] Added interval trigger with {IntervalHours} hours", intervalHours);
        
        // Add startup trigger if AutoSyncOnStartup is enabled
        if (autoSyncOnStartup)
        {
            triggers.Add(new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerStartup
            });
            _logger.LogDebug("[SyncTask] Added startup trigger");
        }
        
        _logger.LogDebug("[SyncTask] Registered {TriggerCount} triggers for SyncTask", triggers.Count);
        return triggers;
    }
}

