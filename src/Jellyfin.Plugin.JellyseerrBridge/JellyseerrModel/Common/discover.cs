using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum DiscoverSliderType
{
    RECENTLY_ADDED = 1,
    RECENT_REQUESTS,
    PLEX_WATCHLIST,
    TRENDING,
    POPULAR_MOVIES,
    MOVIE_GENRES,
    UPCOMING_MOVIES,
    STUDIOS,
    POPULAR_TV,
    TV_GENRES,
    UPCOMING_TV,
    NETWORKS,
    TMDB_MOVIE_KEYWORD,
    TMDB_MOVIE_GENRE,
    TMDB_TV_KEYWORD,
    TMDB_TV_GENRE,
    TMDB_SEARCH,
    TMDB_STUDIO,
    TMDB_NETWORK,
    TMDB_MOVIE_STREAMING_SERVICES,
    TMDB_TV_STREAMING_SERVICES
}

