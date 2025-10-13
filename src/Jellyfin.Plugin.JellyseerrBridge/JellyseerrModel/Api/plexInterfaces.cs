using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class PlexStatus
{
    [JsonPropertyName("settings")]
    public PlexSettings Settings { get; set; } = new();

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

}

public class PlexConnection
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("local")]
    public bool Local { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; } = null!;

    [JsonPropertyName("message")]
    public string? Message { get; set; } = string.Empty;

}

public class PlexDevice
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;

    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("platformVersion")]
    public string PlatformVersion { get; set; } = string.Empty;

    [JsonPropertyName("device")]
    public string Device { get; set; } = string.Empty;

    [JsonPropertyName("clientIdentifier")]
    public string ClientIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("lastSeenAt")]
    public DateTimeOffset LastSeenAt { get; set; }

    [JsonPropertyName("provides")]
    public List<string> Provides { get; set; } = new();

    [JsonPropertyName("owned")]
    public bool Owned { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("publicAddress")]
    public string? PublicAddress { get; set; } = string.Empty;

    [JsonPropertyName("httpsRequired")]
    public bool? HttpsRequired { get; set; } = null!;

    [JsonPropertyName("synced")]
    public bool? Synced { get; set; } = null!;

    [JsonPropertyName("relay")]
    public bool? Relay { get; set; } = null!;

    [JsonPropertyName("dnsRebindingProtection")]
    public bool? DnsRebindingProtection { get; set; } = null!;

    [JsonPropertyName("natLoopbackSupported")]
    public bool? NatLoopbackSupported { get; set; } = null!;

    [JsonPropertyName("publicAddressMatches")]
    public bool? PublicAddressMatches { get; set; } = null!;

    [JsonPropertyName("presence")]
    public bool? Presence { get; set; } = null!;

    [JsonPropertyName("ownerID")]
    public string? OwnerID { get; set; } = string.Empty;

    [JsonPropertyName("home")]
    public bool? Home { get; set; } = null!;

    [JsonPropertyName("sourceTitle")]
    public string? SourceTitle { get; set; } = string.Empty;

    [JsonPropertyName("connection")]
    public List<PlexConnection> Connection { get; set; } = new();

}

