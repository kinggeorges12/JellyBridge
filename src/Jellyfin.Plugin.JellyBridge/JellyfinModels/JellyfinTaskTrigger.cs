using System;
using MediaBrowser.Model.Tasks;
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

        // Determine last run source
        var candidates = new System.Collections.Generic.List<(DateTime time, string source)>();
        if (syncEnd.HasValue) candidates.Add((syncEnd.Value, "Scheduled"));
        if (autoOnStartup && startupEnd.HasValue) candidates.Add((startupEnd.Value, "Startup"));
        if (candidates.Count > 0)
        {
            var latest = candidates.OrderByDescending(c => c.time).First();
            lastRun = latest.time;
            lastRunSource = latest.source;
        }

        // Next run
        if (!isEnabled)
        {
            return (null, lastRunSource, null);
        }

        if (syncTask?.Triggers != null)
        {
            var intervalTicks = syncTask.Triggers
                .Where(t => IsInterval(t) && t.IntervalTicks.HasValue)
                .Select(t => t.IntervalTicks!.Value)
                .Cast<long?>()
                .FirstOrDefault();

            if (intervalTicks.HasValue)
            {
                var interval = TimeSpan.FromTicks(intervalTicks.Value);

                // If the scheduled task was just updated and that time is after the last completed scheduled run,
                // Jellyfin will effectively defer the next run based on the update (behaves like a fresh start).
                if (scheduledTaskTimestamp.HasValue)
                {
                    var timestampUtc = scheduledTaskTimestamp.Value.UtcDateTime;
                    if (!syncEnd.HasValue || timestampUtc > syncEnd.Value)
                    {
                        nextRun = timestampUtc.AddHours(1);
                    }
                }
                
                if (!nextRun.HasValue)
                {
                    if (syncEnd.HasValue)
                    {
                        nextRun = syncEnd.Value.Add(interval);
                    }
                    else if (startupStart.HasValue)
                    {
                        nextRun = startupStart.Value.AddHours(1);
                    }
                    else if (syncStart.HasValue)
                    {
                        nextRun = syncStart.Value.Add(interval);
                    }
                    else
                    {
                        nextRun = null;
                    }
                }
            }
        }

        // Convert to UTC offsets for transport
        DateTimeOffset? lastRunOffset = lastRun.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(lastRun.Value, DateTimeKind.Utc)) : null;
        DateTimeOffset? nextRunOffset = nextRun.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(nextRun.Value, DateTimeKind.Utc)) : null;

        return (lastRunOffset, lastRunSource, nextRunOffset);
    }
}


