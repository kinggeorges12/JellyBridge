using System;
using MediaBrowser.Model.Tasks;
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
}


