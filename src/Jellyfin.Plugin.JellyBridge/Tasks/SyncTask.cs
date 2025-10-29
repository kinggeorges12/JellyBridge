using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Controllers;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Scheduled task for syncing Jellyseerr data.
/// </summary>
public class SyncTask : IScheduledTask
{
    private readonly ILogger<SyncTask> _logger;
    private readonly SyncService _syncService;
    private readonly ITaskManager _taskManager;
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;


    public SyncTask(
        ILogger<SyncTask> logger,
        SyncService syncService,
        ITaskManager taskManager,
        PlaceholderVideoGenerator placeholderVideoGenerator)
    {
        _logger = logger;
        _syncService = syncService;
        _taskManager = taskManager;
        _placeholderVideoGenerator = placeholderVideoGenerator;
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
            _logger.LogInformation("Starting interval sync task");
            
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
                } finally {
                    progress.Report(50);
                }
                
                // Step 2: Sync discover from Jellyseerr (50-100%)
                _logger.LogDebug("Step 2: Syncing discover from Jellyseerr...");
                
                try
                {
                    syncFromResult = await _syncService.SyncFromJellyseerr();
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
                } finally {
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
        var isEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
        var intervalHours = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours));
        
        if (!isEnabled)
        {
            _logger.LogDebug("Plugin is disabled, returning empty triggers");
            return Array.Empty<TaskTriggerInfo>();
        }

        _logger.LogDebug("Added interval trigger with {IntervalHours} hours", intervalHours);
        
        return new List<TaskTriggerInfo>
        {
            JellyfinTaskTrigger.Interval(TimeSpan.FromHours(intervalHours))
        };
    }
}

