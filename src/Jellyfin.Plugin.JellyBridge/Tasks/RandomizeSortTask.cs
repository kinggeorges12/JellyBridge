using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Tasks;

/// <summary>
/// Scheduled task for randomizing discover library sort order by updating NFO dateadded fields.
/// </summary>
public class RandomizeSortTask : IScheduledTask
{
    private readonly DebugLogger<RandomizeSortTask> _logger;
    private readonly MetadataService _metadataService;
    private readonly LibraryService _libraryService;

    public RandomizeSortTask(
        ILogger<RandomizeSortTask> logger,
        MetadataService metadataService,
        LibraryService libraryService)
    {
        _logger = new DebugLogger<RandomizeSortTask>(logger);
        _metadataService = metadataService;
        _libraryService = libraryService;
        _logger.LogInformation("RandomizeSortTask constructor called - task initialized");
    }

    public string Name => "JellyBridge Randomize Sort";
    public string Key => "JellyBridgeRandomizeSort";
    public string Description => "Randomizes discover library sort order by updating Date Added metadata in NFO files";
    public string Category => "JellyBridge";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting randomize sort task");
            
            progress.Report(10);
            
            // Randomize NFO dateadded fields
            await _metadataService.RandomizeNfoDateAddedAsync();
            
            progress.Report(50);
            
            // Refresh library metadata to pick up the changes
            await _libraryService.RefreshBridgeLibrary(fullRefresh: true, refreshImages: false);
            
            progress.Report(100);
            
            _logger.LogInformation("Randomize sort task completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in randomize sort task");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var isEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RandomizeDiscoverSortOrder));
        var intervalHours = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.RandomizeSortIntervalHours));
        
        if (!isEnabled)
        {
            _logger.LogDebug("Randomize sort is disabled, returning empty triggers");
            return Array.Empty<TaskTriggerInfo>();
        }

        _logger.LogDebug("Added interval trigger with {IntervalHours} hours", intervalHours);
        
        return new List<TaskTriggerInfo>
        {
            JellyfinTaskTrigger.Interval(TimeSpan.FromHours(intervalHours))
        };
    }
}

