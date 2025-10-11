using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: PersonCreditCrew
/// </summary>
public class PersonCreditCrew : PersonCredit
{

    [JsonPropertyName("department")]
    public string? Department { get; set; } = null;

    [JsonPropertyName("job")]
    public string? Job { get; set; } = null;

}
