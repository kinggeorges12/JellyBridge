using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public class TmdbMediaResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("genreIds")]
    public List<int> GenreIds { get; set; } = new();

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

}

public class TmdbMovieResult : TmdbMediaResult
{
    [JsonPropertyName("mediaType")]
    public new string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("video")]
    public bool Video { get; set; }

}

public class TmdbTvResult : TmdbMediaResult
{
    [JsonPropertyName("mediaType")]
    public new string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("firstAirDate")]
    public string FirstAirDate { get; set; } = string.Empty;

}

public class TmdbCollectionResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

}

public class TmdbPersonResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("knownFor")]
    // Union type array: (TmdbMovieResult | TmdbTvResult)[]
    public List<object> KnownFor { get; set; } = new();

}

public class TmdbPaginatedResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

}

public class TmdbSearchMultiResponse : TmdbPaginatedResponse
{
    [JsonPropertyName("results")]
    // Union type array: ( | TmdbMovieResult | TmdbTvResult | TmdbPersonResult | TmdbCollectionResult )[]
    public List<object> Results { get; set; } = new();

}

public class TmdbSearchMovieResponse : TmdbPaginatedResponse
{
    [JsonPropertyName("results")]
    public List<TmdbMovieResult> Results { get; set; } = new();

}

public class TmdbSearchTvResponse : TmdbPaginatedResponse
{
    [JsonPropertyName("results")]
    public List<TmdbTvResult> Results { get; set; } = new();

}

public class TmdbUpcomingMoviesResponse : TmdbPaginatedResponse
{
    [JsonPropertyName("dates")]
    public TmdbUpcomingMoviesResponseDates Dates { get; set; } = new();

    [JsonPropertyName("results")]
    public List<TmdbMovieResult> Results { get; set; } = new();

}

public class TmdbExternalIdResponse
{
    [JsonPropertyName("movieResults")]
    public List<TmdbMovieResult> MovieResults { get; set; } = new();

    [JsonPropertyName("tvResults")]
    public List<TmdbTvResult> TvResults { get; set; } = new();

    [JsonPropertyName("personResults")]
    public List<TmdbPersonResult> PersonResults { get; set; } = new();

}

public class TmdbCreditCast
{
    [JsonPropertyName("castId")]
    public int CastId { get; set; }

    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null!;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

}

public class TmdbAggregateCreditCast : TmdbCreditCast
{
    [JsonPropertyName("roles")]
    public List<TmdbAggregateCreditCastRoles> Roles { get; set; } = new();

}

public class TmdbCreditCrew
{
    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null!;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

}

public class TmdbExternalIds
{
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("freebaseMid")]
    public string? FreebaseMid { get; set; } = null!;

    [JsonPropertyName("freebaseId")]
    public string? FreebaseId { get; set; } = null!;

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null!;

    [JsonPropertyName("tvrageId")]
    public string? TvrageId { get; set; } = null!;

    [JsonPropertyName("facebookId")]
    public string? FacebookId { get; set; } = null!;

    [JsonPropertyName("instagramId")]
    public string? InstagramId { get; set; } = null!;

    [JsonPropertyName("twitterId")]
    public string? TwitterId { get; set; } = null!;

}

public class TmdbProductionCompany
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public string OriginCountry { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = null!;

    [JsonPropertyName("description")]
    public string? Description { get; set; } = null!;

}

public class TmdbMovieDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("budget")]
    public int Budget { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbMovieDetailsGenres> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null!;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("productionCompanies")]
    public List<TmdbProductionCompany> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("productionCountries")]
    public List<TmdbMovieDetailsProductionCountries> ProductionCountries { get; set; } = new();

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("releaseDates")]
    public TmdbMovieReleaseResult ReleaseDates { get; set; } = new();

    [JsonPropertyName("revenue")]
    public int Revenue { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; } = null!;

    [JsonPropertyName("spokenLanguages")]
    public List<TmdbMovieDetailsSpokenLanguages> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public bool Video { get; set; }

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("credits")]
    public TmdbMovieDetailsCredits Credits { get; set; } = new();

    [JsonPropertyName("belongsToCollection")]
    public TmdbMovieDetailsBelongsToCollection? BelongsToCollection { get; set; } = null!;

    [JsonPropertyName("externalIds")]
    public TmdbExternalIds ExternalIds { get; set; } = new();

    [JsonPropertyName("videos")]
    public TmdbVideoResult Videos { get; set; } = new();

    [JsonPropertyName("watch/providers")]
    public TmdbMovieDetailsWatchProviders? WatchProviders { get; set; } = null!;

    [JsonPropertyName("keywords")]
    public TmdbMovieDetailsKeywords Keywords { get; set; } = new();

}

public class TmdbVideo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("site")]
    public string Site { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

}

public enum TmdbVideoType
{
    Clip = 0,
    Teaser,
    Trailer,
    Featurette,
    OpeningCredits,
    BehindTheScenes,
    Bloopers
}

public class TmdbTvEpisodeResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("airDate")]
    public object AirDate { get; set; } = null!;

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("productionCode")]
    public string ProductionCode { get; set; } = string.Empty;

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("showId")]
    public int ShowId { get; set; }

    [JsonPropertyName("stillPath")]
    public string StillPath { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("voteCuont")]
    public int VoteCuont { get; set; }

}

public class TmdbTvSeasonResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("airDate")]
    public string AirDate { get; set; } = string.Empty;

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

}

public class TmdbTvDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("contentRatings")]
    public TmdbTvRatingResult ContentRatings { get; set; } = new();

    [JsonPropertyName("createdBy")]
    public List<TmdbTvDetailsCreatedBy> CreatedBy { get; set; } = new();

    [JsonPropertyName("episodeRunTime")]
    public List<int> EpisodeRunTime { get; set; } = new();

    [JsonPropertyName("firstAirDate")]
    public string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("genres")]
    public List<TmdbTvDetailsGenres> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [JsonPropertyName("inProduction")]
    public bool InProduction { get; set; }

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("lastAirDate")]
    public string LastAirDate { get; set; } = string.Empty;

    [JsonPropertyName("lastEpisodeToAir")]
    public TmdbTvEpisodeResult? LastEpisodeToAir { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nextEpisodeToAir")]
    public TmdbTvEpisodeResult? NextEpisodeToAir { get; set; } = null!;

    [JsonPropertyName("networks")]
    public List<TmdbNetwork> Networks { get; set; } = new();

    [JsonPropertyName("numberOfEpisodes")]
    public int NumberOfEpisodes { get; set; }

    [JsonPropertyName("numberOfSeasons")]
    public int NumberOfSeasons { get; set; }

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("productionCompanies")]
    public List<TmdbTvDetailsProductionCompanies> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("productionCountries")]
    public List<TmdbTvDetailsProductionCountries> ProductionCountries { get; set; } = new();

    [JsonPropertyName("spokenLanguages")]
    public List<TmdbTvDetailsSpokenLanguages> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("seasons")]
    public List<TmdbTvSeasonResult> Seasons { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("aggregateCredits")]
    public TmdbTvDetailsAggregateCredits AggregateCredits { get; set; } = new();

    [JsonPropertyName("credits")]
    public TmdbTvDetailsCredits Credits { get; set; } = new();

    [JsonPropertyName("externalIds")]
    public TmdbExternalIds ExternalIds { get; set; } = new();

    [JsonPropertyName("keywords")]
    public TmdbTvDetailsKeywords Keywords { get; set; } = new();

    [JsonPropertyName("videos")]
    public TmdbVideoResult Videos { get; set; } = new();

    [JsonPropertyName("watch/providers")]
    public TmdbTvDetailsWatchProviders? WatchProviders { get; set; } = null!;

}

public class TmdbVideoResult
{
    [JsonPropertyName("results")]
    public List<TmdbVideo> Results { get; set; } = new();

}

public class TmdbTvRatingResult
{
    [JsonPropertyName("results")]
    public List<TmdbRating> Results { get; set; } = new();

}

public class TmdbRating
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public string Rating { get; set; } = string.Empty;

}

public class TmdbMovieReleaseResult
{
    [JsonPropertyName("results")]
    public List<TmdbRelease> Results { get; set; } = new();

}

public class TmdbRelease : TmdbRating
{
    [JsonPropertyName("releaseDates")]
    public List<TmdbReleaseReleaseDates> ReleaseDates { get; set; } = new();

}

public class TmdbKeyword
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbPersonDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("birthday")]
    public string Birthday { get; set; } = string.Empty;

    [JsonPropertyName("deathday")]
    public string Deathday { get; set; } = string.Empty;

    [JsonPropertyName("knownForDepartment")]
    public string KnownForDepartment { get; set; } = string.Empty;

    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; } = new();

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("biography")]
    public string Biography { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("placeOfBirth")]
    public string? PlaceOfBirth { get; set; } = null!;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

}

public class TmdbPersonCredit
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; } = null!;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("firstAirDate")]
    public string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("genreIds")]
    public List<int>? GenreIds { get; set; } = new();

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public bool? Video { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

}

public class TmdbPersonCreditCast : TmdbPersonCredit
{
    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

}

public class TmdbPersonCreditCrew : TmdbPersonCredit
{
    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;

}

public class TmdbPersonCombinedCredits
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("cast")]
    public List<TmdbPersonCreditCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<TmdbPersonCreditCrew> Crew { get; set; } = new();

}

public class TmdbSeasonWithEpisodes : TmdbTvSeasonResult
{
    [JsonPropertyName("episodes")]
    public List<TmdbTvEpisodeResult> Episodes { get; set; } = new();

    [JsonPropertyName("externalIds")]
    public TmdbExternalIds ExternalIds { get; set; } = new();


    // TypeScript: Omit<TmdbTvSeasonResult, 'episode_count'>
}

public class TmdbCollection
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null!;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("parts")]
    public List<TmdbMovieResult> Parts { get; set; } = new();

}

public class TmdbRegion
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("englishName")]
    public string EnglishName { get; set; } = string.Empty;

}

public class TmdbLanguage
{
    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("englishName")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbGenresResult
{
    [JsonPropertyName("genres")]
    public List<TmdbGenre> Genres { get; set; } = new();

}

public class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbNetwork
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = null!;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("originCountry")]
    public string? OriginCountry { get; set; } = null!;

}

public class TmdbWatchProviders
{
    [JsonPropertyName("link")]
    public string? Link { get; set; } = null!;

    [JsonPropertyName("buy")]
    public List<TmdbWatchProviderDetails>? Buy { get; set; } = new();

    [JsonPropertyName("flatrate")]
    public List<TmdbWatchProviderDetails>? Flatrate { get; set; } = new();

}

public class TmdbWatchProviderDetails
{
    [JsonPropertyName("displayPriority")]
    public int? DisplayPriority { get; set; } = null!;

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("providerId")]
    public int ProviderId { get; set; }

    [JsonPropertyName("providerName")]
    public string ProviderName { get; set; } = string.Empty;

}

public class TmdbKeywordSearchResponse : TmdbPaginatedResponse
{
    [JsonPropertyName("results")]
    public List<TmdbKeyword> Results { get; set; } = new();

}

public class TmdbCompany
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbCompanySearchResponse : TmdbPaginatedResponse
{
    [JsonPropertyName("results")]
    public List<TmdbCompany> Results { get; set; } = new();

}

public class TmdbWatchProviderRegion
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("englishName")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("nativeName")]
    public string NativeName { get; set; } = string.Empty;

}



public class TmdbUpcomingMoviesResponseDates
{
    [JsonPropertyName("maximum")]
    public string Maximum { get; set; } = string.Empty;

    [JsonPropertyName("minimum")]
    public string Minimum { get; set; } = string.Empty;

}

public class TmdbAggregateCreditCastRoles
{
    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

}

public class TmdbMovieDetailsGenres
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbMovieDetailsProductionCountries
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbMovieDetailsSpokenLanguages
{
    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbMovieDetailsCredits
{
    [JsonPropertyName("cast")]
    public List<TmdbCreditCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<TmdbCreditCrew> Crew { get; set; } = new();

}

public class TmdbMovieDetailsBelongsToCollection
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

}

public class TmdbMovieDetailsWatchProvidersResults
{
}

public class TmdbMovieDetailsWatchProviders
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("results")]
    public TmdbMovieDetailsWatchProvidersResults? Results { get; set; } = null!;

}

public class TmdbMovieDetailsKeywords
{
    [JsonPropertyName("keywords")]
    public List<TmdbKeyword> Keywords { get; set; } = new();

}

public class TmdbTvDetailsCreatedBy
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

}

public class TmdbTvDetailsGenres
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbTvDetailsProductionCompanies
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public string OriginCountry { get; set; } = string.Empty;

}

public class TmdbTvDetailsProductionCountries
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbTvDetailsSpokenLanguages
{
    [JsonPropertyName("englishName")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TmdbTvDetailsAggregateCredits
{
    [JsonPropertyName("cast")]
    public List<TmdbAggregateCreditCast> Cast { get; set; } = new();

}

public class TmdbTvDetailsCredits
{
    [JsonPropertyName("crew")]
    public List<TmdbCreditCrew> Crew { get; set; } = new();

}

public class TmdbTvDetailsKeywords
{
    [JsonPropertyName("results")]
    public List<TmdbKeyword> Results { get; set; } = new();

}

public class TmdbTvDetailsWatchProvidersResults
{
}

public class TmdbTvDetailsWatchProviders
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("results")]
    public TmdbTvDetailsWatchProvidersResults? Results { get; set; } = null!;

}

public class TmdbReleaseReleaseDates
{
    [JsonPropertyName("certification")]
    public string Certification { get; set; } = string.Empty;

    [JsonPropertyName("iso_639_1")]
    public string? Iso6391 { get; set; } = null!;

    [JsonPropertyName("note")]
    public string? Note { get; set; } = null!;

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

}