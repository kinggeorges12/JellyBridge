using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// A no-op task that always runs on server startup so we can capture an authoritative startup timestamp
/// via TaskManager (StartTimeUtc/EndTimeUtc) regardless of plugin settings.
/// </summary>
public class JellyfinStartTask : IScheduledTask
{
    private readonly ILogger<JellyfinStartTask> _logger;

    public JellyfinStartTask(ILogger<JellyfinStartTask> logger)
    {
        _logger = logger;
    }

    public string Name => "JellyBridge Jellyfin Startup Marker";
    public string Key => "JellyfinStartTask";
    public string Description => "No-op task to record server startup time for scheduling projections";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // No work; just mark completion so TaskManager records Start/End times
        _logger.LogTrace("JellyfinStartTask executed (no-op) to record startup timestamps");
        progress.Report(100);
        await Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Always register a startup trigger, independent of plugin AutoSyncOnStartup
        return new List<TaskTriggerInfo>
        {
            JellyfinTaskTrigger.Startup()
        };
    }
}


