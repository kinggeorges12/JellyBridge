using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: CombinedCredit
/// </summary>
public class CombinedCredit
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("cast")]
    public List<PersonCreditCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<PersonCreditCrew> Crew { get; set; } = new();

}
