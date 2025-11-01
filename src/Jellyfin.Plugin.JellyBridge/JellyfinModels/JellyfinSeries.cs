using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using System.Threading;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Result of attempting to set placeholder episode play count.
/// </summary>
public class PlaceholderEpisodePlayCountResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Wrapper around Jellyfin's Series class.
/// Provides additional functionality for Jellyseerr bridge operations.
/// </summary>
public class JellyfinSeries : WrapperBase<Series>, IJellyfinItem
{
    public JellyfinSeries(Series series) : base(series) 
    {
        InitializeVersionSpecific();
    }

    /// <summary>
    /// Create a JellyfinSeries from a MediaBrowser Series.
    /// </summary>
    /// <param name="series">The MediaBrowser Series to wrap</param>
    /// <returns>A new JellyfinSeries instance</returns>
    public static JellyfinSeries FromSeries(Series series)
    {
        return new JellyfinSeries(series);
    }

    /// <summary>
    /// Create a JellyfinSeries from a BaseItem. Throws ArgumentException if the item is not a Series.
    /// </summary>
    /// <param name="item">The BaseItem to wrap (must be a Series)</param>
    /// <returns>A new JellyfinSeries instance</returns>
    /// <exception cref="ArgumentException">Thrown if the item is not a Series</exception>
    public static JellyfinSeries FromItem(BaseItem item)
    {
        if (item is Series series)
        {
            return new JellyfinSeries(series);
        }
        throw new ArgumentException($"Item is not a Series. Type: {item?.GetType().Name}", nameof(item));
    }

    /// <summary>
    /// Explicit cast from BaseItem to JellyfinSeries.
    /// Returns null if the item is not a Series.
    /// </summary>
    public static explicit operator JellyfinSeries?(BaseItem? item)
    {
        if (item is Series series)
        {
            return new JellyfinSeries(series);
        }
        return null;
    }

    /// <summary>
    /// Get the ID of this series.
    /// </summary>
    public Guid Id => Inner.Id;

    /// <summary>
    /// Get the name of this series.
    /// </summary>
    public string Name => Inner.Name;

    /// <summary>
    /// Get the path of this series.
    /// </summary>
    public string Path => Inner.Path;

    /// <summary>
    /// Get the provider IDs for this series.
    /// </summary>
    public Dictionary<string, string> ProviderIds => Inner.ProviderIds;

    /// <summary>
    /// Extract TMDB ID from series metadata.
    /// </summary>
    /// <returns>TMDB ID if found, null otherwise</returns>
    public int? GetTmdbId()
    {
        try
        {
            if (ProviderIds.TryGetValue("Tmdb", out var providerId) && !string.IsNullOrEmpty(providerId))
            {
                if (int.TryParse(providerId, out var id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // Ignore errors and return null
        }
        
        return null;
    }

    /// <summary>
    /// Get a provider ID by name.
    /// </summary>
    /// <param name="name">The provider name</param>
    /// <returns>The provider ID if found, null otherwise</returns>
    public string? GetProviderId(string name)
    {
        try
        {
            if (ProviderIds.TryGetValue(name, out var providerId))
            {
                return providerId;
            }
        }
        catch
        {
            // Ignore errors and return null
        }
        
        return null;
    }

    /// <summary>
    /// Check if two JellyfinSeries objects match by comparing IDs.
    /// </summary>
    /// <param name="other">Other JellyfinSeries to compare</param>
    /// <returns>True if the series match, false otherwise</returns>
    public bool ItemsMatch(IJellyfinItem other)
    {
        if (other == null) return false;
        return Id == other.Id;
    }

    /// <summary>
    /// Serialize the item to JSON using the provided DTO service.
    /// </summary>
    /// <param name="dtoService">The DTO service to use for serialization</param>
    /// <returns>JSON representation of the item</returns>
    public string? ToJson(MediaBrowser.Controller.Dto.IDtoService dtoService)
    {
        try
        {
            var dtoOptions = new MediaBrowser.Controller.Dto.DtoOptions();
            var baseItemDto = dtoService.GetBaseItemDto(Inner, dtoOptions);
            return System.Text.Json.JsonSerializer.Serialize(baseItemDto);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Get the placeholder episode (S00E00 special) for this series.
    /// </summary>
    /// <returns>The placeholder episode if found, null otherwise</returns>
    public Episode? GetPlaceholderEpisode()
    {
        try
        {
            // Get all episodes for the series by querying children recursively
            // Episodes are children of seasons, which are children of the series
#if JELLYFIN_10_11
            // Jellyfin 10.11+ returns IReadOnlyList<BaseItem>
            var allEpisodes = Inner.GetRecursiveChildren()
                .OfType<Episode>()
                .ToList();
#else
            // Jellyfin 10.10.7 returns IList<BaseItem>
            var allEpisodes = Inner.GetRecursiveChildren()
                .OfType<Episode>()
                .ToList();
#endif
            
            // Find the episode with ParentIndexNumber = 0 (season 0/specials) and IndexNumber = 0 (episode 0)
            return allEpisodes.FirstOrDefault(e => 
                e.ParentIndexNumber == 0 && 
                e.IndexNumber == 0);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Try to mark the placeholder episode (S00E00) as played for a user if it's not already marked.
    /// </summary>
    /// <param name="user">The user to mark the episode as played for</param>
    /// <param name="userDataManager">The user data manager to update play status</param>
    /// <returns>Result object containing success status and detailed information about the operation</returns>
    public PlaceholderEpisodePlayCountResult TrySetEpisodePlayCount(JellyfinUser user, JellyfinIUserDataManager userDataManager)
    {
        // Get the placeholder episode (S00E00)
        var placeholderEpisode = GetPlaceholderEpisode();
        
        if (placeholderEpisode == null)
        {
            return new PlaceholderEpisodePlayCountResult
            {
                Success = false,
                Message = "Placeholder episode (S00E00) not found"
            };
        }
        
        // Check current play status and mark as played if needed
        // This prevents the placeholder episode from appearing in unplayed counts (series badge shows "1")
        var userData = userDataManager.GetUserData(user, placeholderEpisode);
        if (userData == null)
        {
            return new PlaceholderEpisodePlayCountResult
            {
                Success = false,
                Message = $"UserData is null for placeholder episode '{placeholderEpisode.Name}' (Id: {placeholderEpisode.Id})"
            };
        }
        
        if (!userData.Played)
        {
            // Mark as played - this prevents it from appearing in unplayed counts (series badge)
            // Use the inner UserDataManager directly to set Played = true for this specific episode only
            var userEntity = user.Inner;
            userData.Played = true;
            userDataManager.Inner.SaveUserData(userEntity, placeholderEpisode, userData, MediaBrowser.Model.Entities.UserDataSaveReason.Import, System.Threading.CancellationToken.None);
            
            return new PlaceholderEpisodePlayCountResult
            {
                Success = true,
                Message = $"Marked placeholder episode '{placeholderEpisode.Name}' (Id: {placeholderEpisode.Id}) as played"
            };
        }
        
        // Already marked as played, no need to update
        return new PlaceholderEpisodePlayCountResult
        {
            Success = false,
            Message = $"Placeholder episode '{placeholderEpisode.Name}' (Id: {placeholderEpisode.Id}) is already marked as played"
        };
    }
}
