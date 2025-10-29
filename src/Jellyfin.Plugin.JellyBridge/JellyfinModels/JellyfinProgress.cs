using System;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around .NET's Progress class.
/// Version-specific implementation for Jellyfin 10.10.7 with conditional compilation for 10.11+.
/// </summary>
public class JellyfinProgress<T> : WrapperBase<Progress<T>>
{
    public JellyfinProgress(Progress<T> progress) : base(progress) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Version-specific progress operations.
    /// </summary>
    public void PerformProgressOperation(T value)
    {
#if JELLYFIN_V10_11
        // Jellyfin 10.11+ specific progress operations
        PerformV10_11ProgressOperation(value);
#else
        // Jellyfin 10.10.7 specific progress operations
        PerformV10_10_7ProgressOperation(value);
#endif
    }

#if JELLYFIN_V10_11
    /// <summary>
    /// Jellyfin 10.11+ specific progress operations.
    /// </summary>
    private void PerformV10_11ProgressOperation(T value)
    {
        // Future implementation for 10.11+
        // Example: Use new progress API
        // Note: Progress<T> doesn't expose Report method publicly
    }
#else
    /// <summary>
    /// Jellyfin 10.10.7 specific progress operations.
    /// </summary>
    private void PerformV10_10_7ProgressOperation(T value)
    {
        // Current implementation for 10.10.7
        // Example: Use legacy progress API
        // Note: Progress<T> doesn't expose Report method publicly
    }
#endif

    // Custom helper methods can be added here
    // Example: public void ReportProgress(T value) { Inner.Report(value); }
}