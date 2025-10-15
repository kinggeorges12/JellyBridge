using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using System;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Interface for Jellyseerr media items that provides common functionality.
/// </summary>
public interface IJellyseerrItem
{
    /// <summary>
    /// The TMDB ID of the media item.
    /// </summary>
    int Id { get; }

    /// <summary>
    /// The display name of the media item.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The year of the media item.
    /// </summary>
    string Year { get; }

    /// <summary>
    /// The media type from the API response (inherited from base classes).
    /// </summary>
    string MediaType { get; }

    /// <summary>
    /// Jellyseerr-specific media information (download status, service IDs, etc.).
    /// </summary>
    JellyseerrMediaTest? MediaInfo { get; set; }

    /// <summary>
    /// The extra external ID (IMDB for movies, TVDB for shows).
    /// </summary>
    string? ExtraId { get; }

    /// <summary>
    /// The display name for the extra external ID (e.g., "imdbid", "tvdbid").
    /// </summary>
    string ExtraIdName { get; }

    /// <summary>
    /// Extract year from date string.
    /// </summary>
    static string ExtractYear(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return string.Empty;
            
        if (DateTime.TryParse(dateString, out var date))
            return date.Year.ToString();
            
        return string.Empty;
    }
}
