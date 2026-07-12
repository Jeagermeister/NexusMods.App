using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Apocrypha.Sdk.Games;

namespace Apocrypha.DataModel.JsonConverters;

/// <summary>
/// Round-trips <see cref="GameId"/> as its raw <see cref="ulong"/> value. Without an explicit
/// converter System.Text.Json falls back to object binding, whose parameterless-constructor
/// call the value object forbids ("Use GameId.From instead") — which broke restoring any saved
/// window state containing a game-scoped page (e.g. the per-game Downloads page).
/// </summary>
[PublicAPI]
public class GameIdConverter : JsonConverter<GameId>
{
    /// <inheritdoc />
    public override GameId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => GameId.From(reader.GetUInt64());

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, GameId value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}
