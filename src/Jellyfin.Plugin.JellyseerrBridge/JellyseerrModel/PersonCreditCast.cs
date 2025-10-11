using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: PersonCreditCast
/// </summary>
public class PersonCreditCast : PersonCredit
{

    [JsonPropertyName("character")]
    public string? Character { get; set; } = null;

}
