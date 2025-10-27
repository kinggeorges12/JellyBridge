using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;

public class ServiceCommonServer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is4k")]
    public bool Is4k { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("activeProfileId")]
    public int ActiveProfileId { get; set; }

    [JsonPropertyName("activeDirectory")]
    public string ActiveDirectory { get; set; } = string.Empty;

    [JsonPropertyName("activeLanguageProfileId")]
    public int? ActiveLanguageProfileId { get; set; } = null!;

    [JsonPropertyName("activeAnimeProfileId")]
    public int? ActiveAnimeProfileId { get; set; } = null!;

    [JsonPropertyName("activeAnimeDirectory")]
    public string? ActiveAnimeDirectory { get; set; } = null!;

    [JsonPropertyName("activeAnimeLanguageProfileId")]
    public int? ActiveAnimeLanguageProfileId { get; set; } = null!;

    [JsonPropertyName("activeTags")]
    public List<int> ActiveTags { get; set; } = new();

    [JsonPropertyName("activeAnimeTags")]
    public List<int>? ActiveAnimeTags { get; set; } = new();

}

public class ServiceCommonServerWithDetails
{
    [JsonPropertyName("server")]
    public ServiceCommonServer Server { get; set; } = new();

    [JsonPropertyName("profiles")]
    public List<QualityProfile> Profiles { get; set; } = new();

    [JsonPropertyName("rootFolders")]
    // TypeScript: Partial<RootFolder>
    public List<RootFolder> RootFolders { get; set; } = new();

    [JsonPropertyName("languageProfiles")]
    public List<LanguageProfile>? LanguageProfiles { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<Tag> Tags { get; set; } = new();

}

