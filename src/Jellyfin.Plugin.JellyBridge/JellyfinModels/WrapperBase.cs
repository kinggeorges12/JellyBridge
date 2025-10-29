namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Base wrapper class for Jellyfin objects using composition pattern.
/// Version-specific implementation for Jellyfin 10.10.7 with conditional compilation for 10.11+.
/// </summary>
/// <typeparam name="T">The wrapped Jellyfin type</typeparam>
public abstract class WrapperBase<T> where T : class
{
    public readonly T Inner;

    protected WrapperBase(T inner) => Inner = inner;

    /// <summary>
    /// Implicit conversion to the wrapped type for seamless integration.
    /// </summary>
    public static implicit operator T(WrapperBase<T> wrapper) => wrapper.Inner;

    /// <summary>
    /// Version-specific initialization logic.
    /// </summary>
    protected virtual void InitializeVersionSpecific()
    {
#if JELLYFIN_V10_11
        // Jellyfin 10.11+ specific initialization
        InitializeV10_11();
#else
        // Jellyfin 10.10.7 specific initialization
        InitializeV10_10_7();
#endif
    }

#if JELLYFIN_V10_11
    /// <summary>
    /// Jellyfin 10.11+ specific initialization.
    /// </summary>
    protected virtual void InitializeV10_11()
    {
        // Future implementation for 10.11+
    }
#else
    /// <summary>
    /// Jellyfin 10.10.7 specific initialization.
    /// </summary>
    protected virtual void InitializeV10_10_7()
    {
        // Current implementation for 10.10.7
    }
#endif
}
