using System.Text.Json;
using DynamicData.Kernel;
using FluentAssertions;
using Apocrypha.DataModel.JsonConverters;
using Apocrypha.Sdk.Games;
using Xunit;

namespace Apocrypha.DataModel.Tests;

/// <summary>
/// Regression tests for <see cref="GameIdConverter"/>: saved window state serializes page
/// contexts, and a game-scoped page (e.g. the per-game Downloads page) embeds an
/// <c>Optional&lt;GameId&gt;</c>. Without an explicit converter, deserialization hit the value
/// object's throwing parameterless constructor ("Use GameId.From instead") on every startup,
/// which made <c>WindowManager.RestoreWindowState</c> discard the user's saved layout.
/// </summary>
public class GameIdJsonTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new GameIdConverter(), new OptionalConverterFactory() },
    };

    private record GameScopedContext
    {
        public Optional<GameId> GameScope { get; init; } = Optional<GameId>.None;
    }

    [Fact]
    public void GameId_RoundTripsAsNumber()
    {
        var id = GameId.From(1234567890UL);
        var json = JsonSerializer.Serialize(id, Options);
        json.Should().Be("1234567890");
        JsonSerializer.Deserialize<GameId>(json, Options).Should().Be(id);
    }

    [Fact]
    public void OptionalGameId_InContextRecord_RoundTrips()
    {
        var context = new GameScopedContext { GameScope = GameId.From("Risk of Rain 2") };
        var json = JsonSerializer.Serialize(context, Options);
        var restored = JsonSerializer.Deserialize<GameScopedContext>(json, Options);
        restored!.GameScope.Should().Be(context.GameScope);
    }

    [Fact]
    public void OptionalGameId_None_RoundTrips()
    {
        var context = new GameScopedContext();
        var json = JsonSerializer.Serialize(context, Options);
        var restored = JsonSerializer.Deserialize<GameScopedContext>(json, Options);
        restored!.GameScope.HasValue.Should().BeFalse();
    }
}
