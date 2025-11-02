using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Startup task for running enabled automated tasks with delay.
/// </summary>
public class StartupTask : IScheduledTask
{
    private readonly DebugLogger<StartupTask> _logger;
    private readonly SyncService _syncService;
    private readonly ITaskManager _taskManager;

    public StartupTask(
        ILogger<StartupTask> logger,
        SyncService syncService,
        ITaskManager taskManager)
    {
        _logger = new DebugLogger<StartupTask>(logger);
        _syncService = syncService;
        _taskManager = taskManager;
    }

    public string Name => "JellyBridge Startup";
    public string Key => "JellyBridgeStartup";
    public string Description => "Runs enabled automated tasks on plugin startup (Sync and Sort)";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting startup task execution");
            
            var autoSyncOnStartup = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync));
            var startupDelaySeconds = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.StartupDelaySeconds));
            var isSyncEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
            var isSortEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableAutomatedSortTask));
            _logger.LogTrace("Startup config - EnableStartupSync={Auto}, StartupDelaySeconds={Delay}, IsSyncEnabled={SyncEnabled}, IsSortEnabled={SortEnabled}", autoSyncOnStartup, startupDelaySeconds, isSyncEnabled, isSortEnabled);
            
            // Indicate start
            progress.Report(0);

            if (autoSyncOnStartup && startupDelaySeconds > 0)
            {
                _logger.LogDebug("Applying startup delay of {StartupDelaySeconds} seconds", startupDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), cancellationToken);
            }
            
            if (!autoSyncOnStartup)
            {
                _logger.LogTrace("EnableStartupSync disabled - startup task will not execute any tasks");
                progress.Report(100);
                return;
            }

            var tasksExecuted = 0;
            var totalTasks = 0;
            
            // Count enabled tasks
            if (isSyncEnabled) totalTasks++;
            if (isSortEnabled) totalTasks++;
            
            if (totalTasks == 0)
            {
                _logger.LogTrace("No automated tasks are enabled - skipping startup execution");
                progress.Report(100);
                return;
            }

            // Calculate progress ranges: 10% to 100% (90% total), divided equally among tasks
            var progressRangePerTask = 90.0 / totalTasks;
            var currentProgressBase = 10.0;

            // Execute SyncTask if enabled
            if (isSyncEnabled)
            {
                var syncTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSync");
                if (syncTask?.ScheduledTask is SyncTask task)
                {
                    _logger.LogTrace("Found SyncTask; executing from startup task");
                    var taskStart = currentProgressBase;
                    var taskProgress = new Progress<double>(p => progress.Report(taskStart + (p * progressRangePerTask / 100.0)));
                    await task.ExecuteAsync(taskProgress, cancellationToken);
                    tasksExecuted++;
                    currentProgressBase += progressRangePerTask;
                }
                else
                {
                    var available = string.Join(", ", _taskManager.ScheduledTasks.Select(t => t.ScheduledTask.Key));
                    _logger.LogWarning("Could not find SyncTask to execute. Available tasks: {Tasks}", available);
                }
            }

            // Execute SortTask if enabled
            if (isSortEnabled)
            {
                var sortTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSort");
                if (sortTask?.ScheduledTask is SortTask task)
                {
                    _logger.LogTrace("Found SortTask; executing from startup task");
                    var taskStart = currentProgressBase;
                    var taskProgress = new Progress<double>(p => progress.Report(taskStart + (p * progressRangePerTask / 100.0)));
                    await task.ExecuteAsync(taskProgress, cancellationToken);
                    tasksExecuted++;
                    currentProgressBase += progressRangePerTask;
                }
                else
                {
                    var available = string.Join(", ", _taskManager.ScheduledTasks.Select(t => t.ScheduledTask.Key));
                    _logger.LogWarning("Could not find SortTask to execute. Available tasks: {Tasks}", available);
                }
            }

            progress.Report(100);
            _logger.LogInformation("Startup task execution completed - {TasksExecuted} tasks executed", tasksExecuted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in startup task");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Always register a startup trigger. ExecuteAsync will decide whether to run tasks based on configuration.
        _logger.LogTrace("Registering startup trigger for StartupTask");
        return new List<TaskTriggerInfo>
        {
            JellyfinTaskTrigger.Startup()
        };
    }
}