using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

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
    /// Get the underlying Series instance.
    /// </summary>
    /// <returns>The Series instance or null if not available</returns>
    public Series? GetSeries()
    {
        return Inner;
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
}
