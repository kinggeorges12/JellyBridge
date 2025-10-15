using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public class TmdbMediaResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int> GenreIds { get; set; } = new();

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("original_language")]
    public string OriginalLanguage { get; set; } = string.Empty;

}

public class TmdbMovieResult : TmdbMediaResult
{
    [JsonPropertyName("media_type")]
    public new string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("original_title")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("video")]
    public bool Video { get; set; }

}

public class TmdbTvResult : TmdbMediaResult
{
    [JsonPropertyName("media_type")]
    public new string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("original_name")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("origin_country")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("first_air_date")]
    public string FirstAirDate { get; set; } = string.Empty;

}

public class TmdbCollectionResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("original_title")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("original_language")]
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

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("known_for")]
    // Union type array: (TmdbMovieResult | TmdbTvResult)[]
    public List<object> KnownFor { get; set; } = new();

}

public class TmdbPaginatedResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    [JsonPropertyName("total_pages")]
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
    [JsonPropertyName("movie_results")]
    public List<TmdbMovieResult> MovieResults { get; set; } = new();

    [JsonPropertyName("tv_results")]
    public List<TmdbTvResult> TvResults { get; set; } = new();

    [JsonPropertyName("person_results")]
    public List<TmdbPersonResult> PersonResults { get; set; } = new();

}

public class TmdbCreditCast
{
    [JsonPropertyName("cast_id")]
    public int CastId { get; set; }

    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

    [JsonPropertyName("credit_id")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null!;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; } = null!;

}

public class TmdbAggregateCreditCast : TmdbCreditCast
{
    [JsonPropertyName("roles")]
    public List<TmdbAggregateCreditCastRoles> Roles { get; set; } = new();

}

public class TmdbCreditCrew
{
    [JsonPropertyName("credit_id")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null!;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

}

public class TmdbExternalIds
{
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("freebase_mid")]
    public string? FreebaseMid { get; set; } = null!;

    [JsonPropertyName("freebase_id")]
    public string? FreebaseId { get; set; } = null!;

    [JsonPropertyName("tvdb_id")]
    public int? TvdbId { get; set; } = null!;

    [JsonPropertyName("tvrage_id")]
    public string? TvrageId { get; set; } = null!;

    [JsonPropertyName("facebook_id")]
    public string? FacebookId { get; set; } = null!;

    [JsonPropertyName("instagram_id")]
    public string? InstagramId { get; set; } = null!;

    [JsonPropertyName("twitter_id")]
    public string? TwitterId { get; set; } = null!;

}

public class TmdbProductionCompany
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("origin_country")]
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

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("budget")]
    public int Budget { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbMovieDetailsGenres> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

    [JsonPropertyName("original_language")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("original_title")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null!;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("production_companies")]
    public List<TmdbProductionCompany> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("production_countries")]
    public List<TmdbMovieDetailsProductionCountries> ProductionCountries { get; set; } = new();

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("release_dates")]
    public TmdbMovieReleaseResult ReleaseDates { get; set; } = new();

    [JsonPropertyName("revenue")]
    public int Revenue { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; } = null!;

    [JsonPropertyName("spoken_languages")]
    public List<TmdbMovieDetailsSpokenLanguages> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public bool Video { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("credits")]
    public TmdbMovieDetailsCredits Credits { get; set; } = new();

    [JsonPropertyName("belongs_to_collection")]
    public TmdbMovieDetailsBelongsToCollection? BelongsToCollection { get; set; } = null!;

    [JsonPropertyName("external_ids")]
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
    [JsonPropertyName("Clip")]
    Clip = 0,
    [JsonPropertyName("Teaser")]
    Teaser,
    [JsonPropertyName("Trailer")]
    Trailer,
    [JsonPropertyName("Featurette")]
    Featurette,
    [JsonPropertyName("Opening Credits")]
    OpeningCredits,
    [JsonPropertyName("Behind the Scenes")]
    BehindTheScenes,
    [JsonPropertyName("Bloopers")]
    Bloopers
}

public class TmdbTvEpisodeResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("air_date")]
    public object AirDate { get; set; } = null!;

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("production_code")]
    public string ProductionCode { get; set; } = string.Empty;

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("show_id")]
    public int ShowId { get; set; }

    [JsonPropertyName("still_path")]
    public string StillPath { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_cuont")]
    public int VoteCuont { get; set; }

}

public class TmdbTvSeasonResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("air_date")]
    public string AirDate { get; set; } = string.Empty;

    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

}

public class TmdbTvDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("content_ratings")]
    public TmdbTvRatingResult ContentRatings { get; set; } = new();

    [JsonPropertyName("created_by")]
    public List<TmdbTvDetailsCreatedBy> CreatedBy { get; set; } = new();

    [JsonPropertyName("episode_run_time")]
    public List<int> EpisodeRunTime { get; set; } = new();

    [JsonPropertyName("first_air_date")]
    public string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("genres")]
    public List<TmdbTvDetailsGenres> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [JsonPropertyName("in_production")]
    public bool InProduction { get; set; }

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("last_air_date")]
    public string LastAirDate { get; set; } = string.Empty;

    [JsonPropertyName("last_episode_to_air")]
    public TmdbTvEpisodeResult? LastEpisodeToAir { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("next_episode_to_air")]
    public TmdbTvEpisodeResult? NextEpisodeToAir { get; set; } = null!;

    [JsonPropertyName("networks")]
    public List<TmdbNetwork> Networks { get; set; } = new();

    [JsonPropertyName("number_of_episodes")]
    public int NumberOfEpisodes { get; set; }

    [JsonPropertyName("number_of_seasons")]
    public int NumberOfSeasons { get; set; }

    [JsonPropertyName("origin_country")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("original_language")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("original_name")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("production_companies")]
    public List<TmdbTvDetailsProductionCompanies> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("production_countries")]
    public List<TmdbTvDetailsProductionCountries> ProductionCountries { get; set; } = new();

    [JsonPropertyName("spoken_languages")]
    public List<TmdbTvDetailsSpokenLanguages> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("seasons")]
    public List<TmdbTvSeasonResult> Seasons { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("aggregate_credits")]
    public TmdbTvDetailsAggregateCredits AggregateCredits { get; set; } = new();

    [JsonPropertyName("credits")]
    public TmdbTvDetailsCredits Credits { get; set; } = new();

    [JsonPropertyName("external_ids")]
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
    [JsonPropertyName("release_dates")]
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

    [JsonPropertyName("known_for_department")]
    public string KnownForDepartment { get; set; } = string.Empty;

    [JsonPropertyName("also_known_as")]
    public List<string>? AlsoKnownAs { get; set; } = new();

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("biography")]
    public string Biography { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("place_of_birth")]
    public string? PlaceOfBirth { get; set; } = null!;

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

}

public class TmdbPersonCredit
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("original_language")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("origin_country")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("original_name")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; } = null!;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("credit_id")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("first_air_date")]
    public string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int>? GenreIds { get; set; } = new();

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("original_title")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public bool? Video { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("release_date")]
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

    [JsonPropertyName("external_ids")]
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

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("parts")]
    public List<TmdbMovieResult> Parts { get; set; } = new();

}

public class TmdbRegion
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = string.Empty;

}

public class TmdbLanguage
{
    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("english_name")]
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

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("origin_country")]
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
    [JsonPropertyName("display_priority")]
    public int? DisplayPriority { get; set; } = null!;

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("provider_id")]
    public int ProviderId { get; set; }

    [JsonPropertyName("provider_name")]
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

    [JsonPropertyName("logo_path")]
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

    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("native_name")]
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
    [JsonPropertyName("credit_id")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

    [JsonPropertyName("episode_count")]
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

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdrop_path")]
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

    [JsonPropertyName("credit_id")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("profile_path")]
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

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("origin_country")]
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
    [JsonPropertyName("english_name")]
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

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

}