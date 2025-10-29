using System;

#if JELLYFIN_10_11
// Jellyfin version 10.11.*
using JellyfinUserEntity = Jellyfin.Database.Implementations.Entities.User;
#else
// Jellyfin version 10.10.*
using JellyfinUserEntity = Jellyfin.Data.Entities.User;
#endif

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's User class.
/// Version-specific implementation with conditional compilation for User type namespace changes.
/// </summary>
public class JellyfinUser : WrapperBase<JellyfinUserEntity>
{
    public JellyfinUser(JellyfinUserEntity user) : base(user) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Get the username of this user.
    /// </summary>
    public string Username => Inner.Username;

    /// <summary>
    /// Get the ID of this user.
    /// </summary>
    public Guid Id => Inner.Id;
}