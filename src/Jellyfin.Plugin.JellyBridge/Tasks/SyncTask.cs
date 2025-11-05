using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Controllers;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Scheduled task for syncing Jellyseerr data.
/// </summary>
public class SyncTask : IScheduledTask
{
    private readonly DebugLogger<SyncTask> _logger;
    private readonly SyncService _syncService;
    private readonly ITaskManager _taskManager;
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;
    private readonly CleanupService _cleanupService;


    public SyncTask(
        ILogger<SyncTask> logger,
        SyncService syncService,
        ITaskManager taskManager,
        PlaceholderVideoGenerator placeholderVideoGenerator,
        CleanupService cleanupService)
    {
        _logger = new DebugLogger<SyncTask>(logger);
        _syncService = syncService;
        _taskManager = taskManager;
        _placeholderVideoGenerator = placeholderVideoGenerator;
        _cleanupService = cleanupService;
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
                
                // Step 0: Cleanup metadata before sync operations
                progress.Report(5);
                _logger.LogDebug("Step 0: Cleaning up metadata...");
                
                try
                {
                    await _cleanupService.CleanupMetadataAsync();
                    _logger.LogDebug("Step 0: Cleanup completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Step 0 failed: Cleanup metadata");
                    // Continue with sync operations even if cleanup fails
                }
                finally
                {
                    progress.Report(10);
                }
                
                // Step 1: Sync favorites to Jellyseerr
                _logger.LogDebug("Step 1: Syncing favorites to Jellyseerr...");
                
                try
                {
                    syncToResult = await _syncService.SyncToJellyseerr();
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
                    progress.Report(30);
                }
                
                // Step 2: Sync discover from Jellyseerr
                _logger.LogDebug("Step 2: Syncing discover from Jellyseerr...");
                
                try
                {
                    syncFromResult = await _syncService.SyncFromJellyseerr();
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
                    progress.Report(60);
                }

                try {
                    if (syncToResult != null)
                    {
                        _logger.LogInformation("Sync to Jellyseerr result: {Result}", syncToResult.ToString());
                    }
                    if (syncFromResult != null)
                    {
                        _logger.LogInformation("Sync from Jellyseerr result: {Result}", syncFromResult.ToString());
                    }

                    // Apply refresh operations after both syncs are complete
                    await _syncService.ApplyRefreshAsync(syncToResult, syncFromResult);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error applying refresh operations");
                } finally {
                    progress.Report(100);
                }

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

