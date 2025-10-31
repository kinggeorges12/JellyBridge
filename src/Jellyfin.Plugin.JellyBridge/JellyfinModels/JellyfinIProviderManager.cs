using MediaBrowser.Controller.Providers;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's IProviderManager interface.
/// </summary>
public class JellyfinIProviderManager : WrapperBase<IProviderManager>
{
    public JellyfinIProviderManager(IProviderManager providerManager) : base(providerManager)
    {
        InitializeVersionSpecific();
    }

    public void QueueRefresh(Guid itemId, MetadataRefreshOptions options, RefreshPriority priority)
    {
        Inner.QueueRefresh(itemId, options, priority);
    }

    public Task QueueRefreshAsync(Guid itemId, MetadataRefreshOptions options, RefreshPriority priority)
    {
        Inner.QueueRefresh(itemId, options, priority);
        return Task.CompletedTask;
    }
}


