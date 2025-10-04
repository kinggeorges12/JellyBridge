using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using MediaBrowser.Common.ScheduledTasks;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Tasks;

/// <summary>
/// Scheduled task for syncing shows with Jellyseerr.
/// </summary>
public class ShowSyncTask : IScheduledTask
{
    private readonly ShowSyncService _showSyncService;
    private readonly ConfigurationService _configurationService;
    private readonly ILogger<ShowSyncTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShowSyncTask"/> class.
    /// </summary>
    /// <param name="showSyncService">The show sync service.</param>
    /// <param name="configurationService">The configuration service.</param>
    /// <param name="logger">The logger.</param>
    public ShowSyncTask(ShowSyncService showSyncService, ConfigurationService configurationService, ILogger<ShowSyncTask> logger)
    {
        _showSyncService = showSyncService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Jellyseerr Show Sync";

    /// <inheritdoc />
    public string Key => "JellyseerrShowSync";

    /// <inheritdoc />
    public string Description => "Syncs shows from Jellyseerr and creates placeholder directories";

    /// <inheritdoc />
    public string Category => "Jellyseerr Bridge";

    /// <inheritdoc />
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        try
        {
            _logger.LogInformation("Starting scheduled show sync task");

            var config = _configurationService.GetConfiguration();
            if (!config.IsEnabled)
            {
                _logger.LogInformation("Plugin is disabled, skipping scheduled sync");
                progress.Report(100);
                return;
            }

            progress.Report(10);

            var success = await _showSyncService.SyncAllShowsAsync();
            
            progress.Report(100);

            if (success)
            {
                _logger.LogInformation("Scheduled show sync completed successfully");
            }
            else
            {
                _logger.LogError("Scheduled show sync failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled show sync");
            progress.Report(100);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = _configurationService.GetConfiguration();
        var intervalHours = config.SyncIntervalHours;

        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
            }
        };
    }
}
