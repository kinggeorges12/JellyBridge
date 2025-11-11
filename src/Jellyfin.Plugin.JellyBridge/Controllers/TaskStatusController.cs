using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Model.Tasks;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class TaskStatusController : ControllerBase
    {
        private readonly DebugLogger<TaskStatusController> _logger;
        private readonly ITaskManager _taskManager;

        public TaskStatusController(ILoggerFactory loggerFactory, ITaskManager taskManager)
        {
            _logger = new DebugLogger<TaskStatusController>(loggerFactory.CreateLogger<TaskStatusController>());
            _taskManager = taskManager;
        }

        /// <summary>
        /// Get the current status of the scheduled sync task.
        /// </summary>
        [HttpGet("TaskStatus")]
        public IActionResult GetTaskStatus()
        {
            _logger.LogTrace("Task status requested");
            
            try
            {
                // Check if any operation is currently running
                var isRunning = Plugin.IsOperationRunning;
                
                // Try to get the scheduled task workers (used only for progress and nextRun interval)
                var syncTaskWrapper = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSync");
                DateTimeOffset? lastRun;
                DateTimeOffset? nextRun;
                string? lastRunSource; // "Scheduled" or "Startup"

                // Determine last run from TaskManager: consider scheduled and startup tasks
                var startupTaskWrapper = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeStartup");

                // Config flags
                var autoSyncOnStartupEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync));
                var isPluginEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
                // Read nullable timestamp directly from configuration as it is nullable
                var scheduledTaskTimestamp = Plugin.GetConfiguration().ScheduledTaskTimestamp;

                // Delegate timestamp calculation to JellyfinModels helper
                (lastRun, lastRunSource, nextRun) = JellyfinTaskTrigger.CalculateTimestamps(
                    syncTaskWrapper,
                    startupTaskWrapper,
                    isPluginEnabled,
                    autoSyncOnStartupEnabled,
                    scheduledTaskTimestamp
                );
                
                // Normalize times to consistent UTC ISO strings for cross-version compatibility (10.10 vs 10.11)
                // Determine status: Disabled takes precedence, then Running, then Idle
                string status;
                string message;
                if (!isPluginEnabled)
                {
                    status = "Disabled";
                    message = "Plugin is disabled";
                }
                else if (isRunning)
                {
                    status = "Running";
                    message = "Sync operation in progress...";
                }
                else
                {
                    status = "Idle";
                    message = "No active sync operation";
                }
                
                var result = new
                {
                    isRunning = isRunning,
                    status = status,
                    progress = syncTaskWrapper?.CurrentProgress,
                    message = message,
                    // Return UTC offsets; frontend will localize
                    lastRun = lastRun,
                    nextRun = nextRun,
                    lastRunSource = lastRunSource
                };
                
                _logger.LogTrace("Task status: {Status}, LastRun: {LastRun}, NextRun: {NextRun}", result.status, lastRun, nextRun);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get task status");
                return StatusCode(500, new { 
                    isRunning = false,
                    status = "Error",
                    progress = 0,
                    message = $"Failed to get task status: {ex.Message}",
                    lastRun = (DateTime?)null,
                    nextRun = (DateTime?)null
                });
            }
        }
    }
}

