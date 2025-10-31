using MediaBrowser.Model.Tasks;
using System.Collections.Generic;
using System.Linq;
#if JELLYFIN_10_11
using TaskTriggerInfoType = MediaBrowser.Model.Tasks.TaskTriggerInfoType;
#endif

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Compatibility helpers for creating and inspecting TaskTriggerInfo across Jellyfin versions.
/// Uses conditional compilation to handle 10.10.* (string Type with constants) and 10.11.* (enum Type).
/// </summary>
public static class JellyfinTaskTrigger
{
    public static TaskTriggerInfo Startup()
    {
#if JELLYFIN_10_11
        // Jellyfin version 10.11.* - TaskTriggerInfoType is an enum
        return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
#else
        // Jellyfin version 10.10.* - Type is a string with constants
        return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerStartup
        };
#endif
    }

    public static TaskTriggerInfo Interval(TimeSpan interval)
    {
#if JELLYFIN_10_11
        // Jellyfin version 10.11.* - TaskTriggerInfoType is an enum
        return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = interval.Ticks
        };
#else
        // Jellyfin version 10.10.* - Type is a string with constants
        return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = interval.Ticks
        };
#endif
    }

    public static bool IsInterval(TaskTriggerInfo trigger)
    {
#if JELLYFIN_10_11
        // Jellyfin version 10.11.* - TaskTriggerInfoType is an enum
        return trigger.Type == TaskTriggerInfoType.IntervalTrigger;
#else
        // Jellyfin version 10.10.* - Type is a string with constants
        return trigger.Type == TaskTriggerInfo.TriggerInterval;
#endif
    }

    /// <summary>
    /// Calculate last run and next run timestamps for status display, based on task wrappers and configuration.
    ///
    /// Next Run logic:
    /// 1) If the plugin is enabled and the sync task has been run before, add the sync interval to this value:
    ///    the most recent timestamp from ScheduledTaskTimestamp, sync task last run, plugin start task.
    /// 2) Else if the plugin is enabled and sync has not been run before, use plugin startup time + 1 hour.
    /// 3) Else the plugin is not enabled, return null.
    ///
    /// Last run logic:
    /// 1) If the plugin is enabled and the autosync on startup is disabled, only use last run time for scheduled sync.
    /// 2) Else if the plugin is enabled and autosync on startup is enabled, choose the most recent last run time between startup and scheduled tasks.
    /// 3) Else the plugin is disabled, only return last runtime of scheduled sync.
    ///
    /// Returns UTC DateTimeOffset values for frontend localization.
    /// </summary>
    public static (DateTimeOffset? lastRun, string? lastRunSource, DateTimeOffset? nextRun) CalculateTimestamps(
        IScheduledTaskWorker? syncTask,
        IScheduledTaskWorker? startupTask,
        bool isEnabled,
        bool autoOnStartup,
        DateTimeOffset? scheduledTaskTimestamp = null)
    {
        DateTime? lastRun = null;
        string? lastRunSource = null;
        DateTime? nextRun = null;

        DateTime? syncEnd = (syncTask?.LastExecutionResult?.EndTimeUtc is DateTime se && se > DateTime.MinValue) ? se : (DateTime?)null;
        DateTime? syncStart = (syncTask?.LastExecutionResult?.StartTimeUtc is DateTime ss && ss > DateTime.MinValue) ? ss : (DateTime?)null;
        DateTime? startupEnd = (startupTask?.LastExecutionResult?.EndTimeUtc is DateTime ste && ste > DateTime.MinValue) ? ste : (DateTime?)null;
        DateTime? startupStart = (startupTask?.LastExecutionResult?.StartTimeUtc is DateTime sts && sts > DateTime.MinValue) ? sts : (DateTime?)null;

		// LAST RUN LOGIC BRANCHES (see summary above):
		// 1) Plugin enabled + autosync on startup disabled -> only scheduled sync last run counts
		// 2) Plugin enabled + autosync on startup enabled -> use most recent of startup vs scheduled
		// 3) Plugin disabled -> only scheduled sync last run counts
		if (!isEnabled)
        {
            if (syncEnd.HasValue)
            {
                lastRun = syncEnd.Value;
				lastRunSource = "Scheduled"; // (3) plugin disabled -> scheduled only
            }
        }
        else if (!autoOnStartup)
        {
            if (syncEnd.HasValue)
            {
                lastRun = syncEnd.Value;
				lastRunSource = "Scheduled"; // (1) enabled + auto-startup disabled -> scheduled only
            }
        }
        else
        {
            var candidates = new List<(DateTime time, string source)>();
            if (syncEnd.HasValue) candidates.Add((syncEnd.Value, "Scheduled"));
            if (startupEnd.HasValue) candidates.Add((startupEnd.Value, "Startup"));
            if (candidates.Count > 0)
            {
                var latest = candidates.OrderByDescending(c => c.time).First();
				lastRun = latest.time; // (2) enabled + auto-startup enabled -> pick most recent
				lastRunSource = latest.source;
            }
        }

		// NEXT RUN LOGIC BRANCHES (see summary above):
		// 1) Enabled + sync has run before -> add interval to most recent baseline (config timestamp, sync last run (start or end time), startup last run + 1 minute)
		// 2) Enabled + sync has not run before -> startup time + 1 hour
		// 3) Disabled -> nextRun stays null (handled by not entering this block)
		if (isEnabled && syncTask?.Triggers != null)
        {
            var intervalTicks = syncTask.Triggers
                .Where(t => IsInterval(t) && t.IntervalTicks.HasValue)
                .Select(t => t.IntervalTicks!.Value)
                .Cast<long?>()
                .FirstOrDefault();

			if (intervalTicks.HasValue)
            {
                var interval = TimeSpan.FromTicks(intervalTicks.Value);

                // Determine whether sync has ever run before
                bool syncHasRunBefore = syncEnd.HasValue || syncStart.HasValue;

                if (syncHasRunBefore)
                {
                    // Baseline: most recent among ScheduledTaskTimestamp, sync last run (start or end), startup completion + 1 minute
                    var baselineCandidates = new List<DateTime>();
                    if (scheduledTaskTimestamp.HasValue) baselineCandidates.Add(scheduledTaskTimestamp.Value.UtcDateTime);
                    if (syncEnd.HasValue) baselineCandidates.Add(syncEnd.Value);
                    if (syncStart.HasValue) baselineCandidates.Add(syncStart.Value);
                    if (startupEnd.HasValue) baselineCandidates.Add(startupEnd.Value.AddMinutes(1));

                    if (baselineCandidates.Count > 0)
                    {
                        var baseline = baselineCandidates.Max();
                        nextRun = baseline.Add(interval);
                    }
                }
                else
                {
                    // No prior syncs: next run is startup + 1 hour; if no startup timestamps, use now + 1 hour
                    var startupBaseline = startupEnd ?? startupStart;
                    nextRun = (startupBaseline ?? DateTime.UtcNow).AddHours(1);
                }
            }
        }

        // Convert to UTC offsets for transport
        DateTimeOffset? lastRunOffset = lastRun.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(lastRun.Value, DateTimeKind.Utc)) : null;
        DateTimeOffset? nextRunOffset = nextRun.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(nextRun.Value, DateTimeKind.Utc)) : null;

        return (lastRunOffset, lastRunSource, nextRunOffset);
    }
}


