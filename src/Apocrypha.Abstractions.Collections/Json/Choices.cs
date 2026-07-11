using System.Text.Json.Serialization;
using Apocrypha.Games.FOMOD;

namespace Apocrypha.Abstractions.Collections.Json;

public class Choices
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ChoicesType Type { get; init; }

    [JsonPropertyName("options")]
    public FomodOption[] Options { get; init; } = [];
}
