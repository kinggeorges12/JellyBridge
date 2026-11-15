using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels;

/// <summary>
/// Wrapper around Jellyfin's Season class.
/// A season has no TMDB id of its own; the request target is the parent series' TMDB id plus the season number.
/// </summary>
public class JellyfinSeason : WrapperBase<Season>, IJellyfinItem
{
    private static readonly Regex TmdbFolderRegex = new(@"\[tmdbid-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The BaseItemKind type for seasons.
    /// </summary>
    public BaseItemKind TypeName => BaseItemKind.Season;

    public JellyfinSeason(Season season) : base(season)
    {
        InitializeVersionSpecific();
    }

    public static JellyfinSeason FromSeason(Season season) => new JellyfinSeason(season);

    public Guid Id => Inner.Id;

    public string Name => Inner.Name;

    public string Path => Inner.Path;

    /// <summary>
    /// True for a missing season created by Jellyfin's "Import missing episodes" option.
    /// </summary>
    public bool IsVirtual => Inner.IsVirtualItem;

    public Dictionary<string, string> ProviderIds => Inner.ProviderIds;

    /// <summary>
    /// Name of the parent series (for logging).
    /// </summary>
    public string SeriesName => Inner.SeriesName ?? Inner.Series?.Name ?? string.Empty;

    /// <summary>
    /// The season number: Jellyfin's IndexNumber, falling back to the "Season NN" folder name.
    /// </summary>
    public int? SeasonNumber
    {
        get
        {
            if (Inner.IndexNumber.HasValue)
            {
                return Inner.IndexNumber.Value;
            }
            return Services.PlaceholderVideoGenerator.ParseSeasonFolderNumber(Path);
        }
    }

    /// <summary>
    /// TMDB id of the parent series (a season has none of its own).
    /// Falls back to the "[tmdbid-N]" tag in the series folder name.
    /// </summary>
    public int? GetTmdbId()
    {
        try
        {
            var series = Inner.Series;
            if (series != null && series.ProviderIds.TryGetValue("Tmdb", out var providerId)
                && int.TryParse(providerId, out var id))
            {
                return id;
            }
        }
        catch
        {
            // Fall through to the folder name
        }

        try
        {
            var seriesFolder = System.IO.Path.GetDirectoryName((Path ?? string.Empty).TrimEnd('/', '\\'));
            var match = TmdbFolderRegex.Match(System.IO.Path.GetFileName(seriesFolder) ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var folderId))
            {
                return folderId;
            }
        }
        catch
        {
            // Ignore and return null
        }

        return null;
    }

    public string? GetProviderId(string name)
    {
        return ProviderIds.TryGetValue(name, out var value) ? value : null;
    }

    public bool ItemsMatch(IJellyfinItem other)
    {
        return other != null && Id == other.Id;
    }

    public string? ToJson(MediaBrowser.Controller.Dto.IDtoService dtoService)
    {
        try
        {
            var baseItemDto = dtoService.GetBaseItemDto(Inner, new MediaBrowser.Controller.Dto.DtoOptions());
            return System.Text.Json.JsonSerializer.Serialize(baseItemDto);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Mark the season's placeholder episodes as not played (season placeholders must never be Played).
    /// Marking a season placeholder as played is refused on purpose: a played regular-season episode
    /// would put the Discover show into Next Up.
    /// </summary>
    public JellyfinWrapperResult TrySetEpisodePlayCount(JellyfinUser user, JellyfinIUserDataManager userDataManager, bool markAsPlayed)
    {
        if (markAsPlayed)
        {
            return new JellyfinWrapperResult { Success = false, Message = $"Season placeholder '{Name}' is never marked as played" };
        }

        var changed = 0;
        foreach (var episode in Inner.GetRecursiveChildren().OfType<Episode>())
        {
            var userData = userDataManager.GetUserData(user, episode);
            if (userData == null || (!userData.Played && userData.PlaybackPositionTicks == 0))
            {
                continue;
            }
            userData.Played = false;
            userData.PlaybackPositionTicks = 0;
            userDataManager.Inner.SaveUserData(user.Inner, episode, userData, MediaBrowser.Model.Entities.UserDataSaveReason.Import, System.Threading.CancellationToken.None);
            changed++;
        }

        return new JellyfinWrapperResult
        {
            Success = true,
            Message = $"Reset {changed} placeholder episode(s) of season '{Name}' to not played"
        };
    }
}
