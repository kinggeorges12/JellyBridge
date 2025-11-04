using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Scheduled task for sorting discover library by updating play counts for all users.
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
    public string Description => "Sorts discover library by updating play counts for all users";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting sort task");
            
            // Use Jellyfin-style locking that pauses instead of canceling
            await Plugin.ExecuteWithLockAsync<object?>(async () =>
            {
                progress.Report(10);
                
                // Sort discover library by updating play counts for all users
                var sortResult = await _sortService.SortJellyBridge();
                
                progress.Report(50);
                
                // Refresh library to reload user data (play counts) if refresh is needed
                if (sortResult.Refresh != null)
                {
                    await _libraryService.RefreshBridgeLibrary(refreshUserData: false);
                }
                
                progress.Report(100);
                
                _logger.LogInformation("Sort task completed: {Result}", sortResult.ToString());
                
                return null;
            }, _logger, "Sort Task");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in sort task");
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

