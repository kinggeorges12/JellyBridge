using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Controllers;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge;
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
        _logger.LogInformation("JellyseerrSyncTask constructor called - task initialized");
    }

    public string Name => "Jellyseerr Bridge Sync";
    public string Key => "JellyseerrBridgeSync";
    public string Description => "Syncs favorites to Jellyseerr and discovers content from Jellyseerr to Jellyfin";
    public string Category => "Jellyseerr Bridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting scheduled Jellyseerr sync task");
            
            // Use Jellyfin-style locking that pauses instead of canceling
            await Plugin.ExecuteWithLockAsync(async () =>
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
        
        _logger.LogInformation("[JellyseerrSyncTask] GetDefaultTriggers called - IsEnabled: {IsEnabled}, AutoSyncOnStartup: {AutoSyncOnStartup}", isEnabled, autoSyncOnStartup);
        
        if (!isEnabled)
        {
            _logger.LogInformation("[JellyseerrSyncTask] Plugin is disabled, returning empty triggers");
            return Array.Empty<TaskTriggerInfo>();
        }

        var triggers = new List<TaskTriggerInfo>();
        
        // Add interval trigger for regular syncing
        var intervalHours = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours));
        triggers.Add(new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
        });
        
        _logger.LogInformation("[JellyseerrSyncTask] Added interval trigger with {IntervalHours} hours", intervalHours);
        
        // Add startup trigger if AutoSyncOnStartup is enabled
        if (autoSyncOnStartup)
        {
            triggers.Add(new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerStartup
            });
            _logger.LogInformation("[JellyseerrSyncTask] Added startup trigger");
        }
        
        _logger.LogInformation("[JellyseerrSyncTask] Registered {TriggerCount} triggers for JellyseerrSyncTask", triggers.Count);
        return triggers;
    }
}

