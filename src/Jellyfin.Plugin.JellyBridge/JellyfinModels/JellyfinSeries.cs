using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's Series class.
/// Version-specific implementation for Jellyfin 10.10.7 with conditional compilation for 10.11+.
/// </summary>
public class JellyfinSeries : WrapperBase<Series>
{
    public JellyfinSeries(Series series) : base(series) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Version-specific series operations.
    /// </summary>
    public void PerformSeriesOperation()
    {
#if JELLYFIN_V10_11
        // Jellyfin 10.11+ specific series operations
        PerformV10_11SeriesOperation();
#else
        // Jellyfin 10.10.7 specific series operations
        PerformV10_10_7SeriesOperation();
#endif
    }

#if JELLYFIN_V10_11
    /// <summary>
    /// Jellyfin 10.11+ specific series operations.
    /// </summary>
    private void PerformV10_11SeriesOperation()
    {
        // Future implementation for 10.11+
        // Example: Use new series API
    }
#else
    /// <summary>
    /// Jellyfin 10.10.7 specific series operations.
    /// </summary>
    private void PerformV10_10_7SeriesOperation()
    {
        // Current implementation for 10.10.7
        // Example: Use legacy series API
    }
#endif

    // Custom helper methods can be added here
    // Example: public void SyncSeriesMetadata() { /* custom logic */ }
}