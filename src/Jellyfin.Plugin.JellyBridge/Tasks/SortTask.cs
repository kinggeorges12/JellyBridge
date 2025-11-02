using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Scheduled task for randomizing discover library sort order by updating play counts for all users.
/// </summary>
public class SortTask : IScheduledTask
{
    private readonly DebugLogger<SortTask> _logger;
    private readonly SortService _sortService;
    private readonly LibraryService _libraryService;

    public SortTask(
        ILogger<SortTask> logger,
        SortService sortService,
        LibraryService libraryService)
    {
        _logger = new DebugLogger<SortTask>(logger);
        _sortService = sortService;
        _libraryService = libraryService;
        _logger.LogInformation("SortTask constructor called - task initialized");
    }

    public string Name => "JellyBridge Sort";
    public string Key => "JellyBridgeSort";
    public string Description => "Randomizes discover library sort order by updating play counts for all users";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting randomize sort task");
            
            // Use Jellyfin-style locking that pauses instead of canceling
            await Plugin.ExecuteWithLockAsync<object?>(async () =>
            {
                progress.Report(10);
                
                // Randomize play counts for all users to enable random sorting
                var (successes, failures, skipped) = await _sortService.ApplyPlayCountAlgorithmAsync();
                
                progress.Report(50);
                
                // Refresh library to reload user data (play counts)
                await _libraryService.RefreshBridgeLibrary(refreshUserData: false);
                
                progress.Report(100);
                
                _logger.LogInformation("Randomize sort task completed successfully - {SuccessCount} items randomized, {FailureCount} failures, {SkippedCount} skipped", successes.Count, failures.Count, skipped.Count);
                
                return null;
            }, _logger, "Sort Task");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in randomize sort task");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var isEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableAutomatedSortTask));
        var intervalHours = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SortTaskIntervalHours));
        
        if (!isEnabled)
        {
            _logger.LogDebug("Automated sort task is disabled, returning empty triggers");
            return Array.Empty<TaskTriggerInfo>();
        }

        _logger.LogDebug("Added interval trigger with {IntervalHours} hours", intervalHours);
        
        return new List<TaskTriggerInfo>
        {
            JellyfinTaskTrigger.Interval(TimeSpan.FromHours(intervalHours))
        };
    }
}

