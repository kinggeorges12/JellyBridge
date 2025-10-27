using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel;

public class RequestPermissionError : Exception
{
}

public class QuotaRestrictedError : Exception
{
}

public class DuplicateMediaRequestError : Exception
{
}

public class NoSeasonsAvailableError : Exception
{
}

public class BlacklistedMediaError : Exception
{
}



public class MediaRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("status")]
    public MediaRequestStatus Status { get; set; } = new();

    [JsonPropertyName("media")]
    public Media Media { get; set; } = new();

    [JsonPropertyName("requestedBy")]
    public User RequestedBy { get; set; } = new();

    [JsonPropertyName("modifiedBy")]
    public User? ModifiedBy { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("type")]
    public MediaType Type { get; set; } = new();

    // Calculated property removed. Typescript definition then C# property in comments below.
    // @RelationCount((request: MediaRequest) => request.seasons)
    //   public seasonCount: number;
    // [JsonPropertyName("seasonCount")]
    // public int SeasonCount { get; set; }

    [JsonPropertyName("seasons")]
    public List<SeasonRequest> Seasons { get; set; } = new();

    [JsonPropertyName("is4k")]
    public bool Is4k { get; set; }

    [JsonPropertyName("serverId")]
    public int? ServerId { get; set; } = null!;

    [JsonPropertyName("profileId")]
    public int? ProfileId { get; set; } = null!;

    [JsonPropertyName("rootFolder")]
    public string? RootFolder { get; set; } = null!;

    [JsonPropertyName("languageProfileId")]
    public int? LanguageProfileId { get; set; } = null!;

    [JsonPropertyName("tags")]
    public List<int>? Tags { get; set; } = new();

    [JsonPropertyName("isAutoRequest")]
    public bool IsAutoRequest { get; set; }

}


public class MediaRequestOptions
{
    [JsonPropertyName("isAutoRequest")]
    public bool? IsAutoRequest { get; set; } = null!;

}


