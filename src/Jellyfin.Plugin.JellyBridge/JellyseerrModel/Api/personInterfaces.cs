using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;

public class PersonCombinedCreditsResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("cast")]
    public List<PersonCreditCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<PersonCreditCrew> Crew { get; set; } = new();

}


