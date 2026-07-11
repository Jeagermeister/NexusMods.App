using System.Text.Json.Serialization;
using Apocrypha.Abstractions.GOG.DTOs;

namespace Apocrypha.Networking.GOG.DTOs;

internal class DepotResponse
{
    [JsonPropertyName("depot")]
    public required DepotInfo Depot { get; init; }
        
    [JsonPropertyName("version")]
    public required int Version { get; init; }
}
