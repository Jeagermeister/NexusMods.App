using System.Text.Json.Serialization;
using Apocrypha.Abstractions.GOG.DTOs;

namespace Apocrypha.Networking.GOG.DTOs;

internal class BuildListResponse
{
    [JsonPropertyName("items")]
    public required Build[] Items { get; init; }
}
