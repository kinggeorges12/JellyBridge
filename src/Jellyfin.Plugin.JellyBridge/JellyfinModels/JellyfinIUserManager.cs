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

    /// <summary>
    /// Gets a user by their Guid ID. Returns null if not found.
    /// </summary>
    public JellyfinUser? GetUserById(Guid id)
    {
        var user = Inner.GetUserById(id);
        return user != null ? new JellyfinUser((dynamic)user) : null;
    }
}

