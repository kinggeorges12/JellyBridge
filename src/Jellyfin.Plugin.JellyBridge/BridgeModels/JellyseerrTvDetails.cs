using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Minimal model of Seerr's GET /api/v1/tv/{tmdbId} response.
/// Only the fields needed to build per-season placeholders are mapped; status values are kept as
/// plain integers so unknown enum values never break deserialization.
/// Seerr MediaStatus: 1 UNKNOWN, 2 PENDING, 3 PROCESSING, 4 PARTIALLY_AVAILABLE, 5 AVAILABLE, 6 BLOCKLISTED, 7 DELETED.
/// Seerr MediaRequestStatus: 1 PENDING, 2 APPROVED, 3 DECLINED, 4 FAILED, 5 COMPLETED.
/// </summary>
public class JellyseerrTvDetails
{
    public const int MediaStatusPending = 2;
    public const int MediaStatusProcessing = 3;
    public const int MediaStatusAvailable = 5;
    public const int MediaStatusBlocklisted = 6;
    public const int RequestStatusDeclined = 3;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("numberOfSeasons")]
    public int? NumberOfSeasons { get; set; }

    [JsonPropertyName("seasons")]
    public List<JellyseerrTvSeason> Seasons { get; set; } = new();

    [JsonPropertyName("mediaInfo")]
    public JellyseerrTvMediaInfo? MediaInfo { get; set; }

    /// <summary>
    /// Season numbers (> 0) that should get a placeholder: not already available, pending, processing,
    /// blocklisted, or part of a non-declined request. Partially available / unknown / deleted seasons are kept.
    /// </summary>
    public List<JellyseerrTvSeason> GetRequestableSeasons()
    {
        var blockedStatus = new HashSet<int> { MediaStatusPending, MediaStatusProcessing, MediaStatusAvailable, MediaStatusBlocklisted };
        var unavailable = new HashSet<int>();
        foreach (var season in MediaInfo?.Seasons ?? new List<JellyseerrTvSeasonStatus>())
        {
            if (season.Status.HasValue && blockedStatus.Contains(season.Status.Value))
            {
                unavailable.Add(season.SeasonNumber);
            }
        }
        foreach (var request in MediaInfo?.Requests ?? new List<JellyseerrTvRequest>())
        {
            if (request.Status == RequestStatusDeclined)
            {
                continue;
            }
            foreach (var season in request.Seasons ?? new List<JellyseerrTvSeasonStatus>())
            {
                unavailable.Add(season.SeasonNumber);
            }
        }

        return (Seasons ?? new List<JellyseerrTvSeason>())
            .Where(s => s.SeasonNumber > 0 && !unavailable.Contains(s.SeasonNumber))
            .GroupBy(s => s.SeasonNumber)
            .Select(g => g.First())
            .OrderBy(s => s.SeasonNumber)
            .ToList();
    }
}

public class JellyseerrTvSeason
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeCount")]
    public int? EpisodeCount { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("airDate")]
    public string? AirDate { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
}

public class JellyseerrTvMediaInfo
{
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("seasons")]
    public List<JellyseerrTvSeasonStatus> Seasons { get; set; } = new();

    [JsonPropertyName("requests")]
    public List<JellyseerrTvRequest> Requests { get; set; } = new();
}

public class JellyseerrTvSeasonStatus
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}

public class JellyseerrTvRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("seasons")]
    public List<JellyseerrTvSeasonStatus> Seasons { get; set; } = new();
}
