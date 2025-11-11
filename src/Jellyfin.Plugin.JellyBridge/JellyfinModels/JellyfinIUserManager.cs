using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's IUserManager interface.
/// </summary>
public class JellyfinIUserManager : WrapperBase<IUserManager>
{
    public JellyfinIUserManager(IUserManager userManager) : base(userManager)
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Gets all users.
    /// </summary>
    public IEnumerable<JellyfinUser> GetAllUsers()
    {
        return Inner.Users.Select(user => new JellyfinUser((dynamic)user));
    }
}

