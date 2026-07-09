using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Sdk.Games;
using Xunit;

namespace NexusMods.Games.BepInEx.Tests;

/// <summary>
/// The DI edge the family is built around: <c>AddGame&lt;T&gt;()</c> registers one singleton per
/// type, so the family's explicit per-row registrations must uphold the same identity
/// contract — every row resolves to ONE shared instance behind both <see cref="IGame"/> and
/// <see cref="IGameData"/> (DESIGN-bepinex-family.md §2.2).
/// </summary>
public class RegistrationTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBepInExGames();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddBepInExGames_RegistersOneDistinctInstancePerGame()
    {
        using var provider = Build();

        var games = provider.GetServices<IGame>().ToArray();
        var gameData = provider.GetServices<IGameData>().ToArray();

        games.Should().HaveCountGreaterThan(150);
        games.Should().OnlyHaveUniqueItems(because: "every row must construct its own instance");
        games.Select(g => g.GameId).Should().OnlyHaveUniqueItems();

        // Identity contract: IGame and IGameData resolve to the same singletons
        // (both sequences follow registration order, which is pairwise).
        gameData.Should().HaveCount(games.Length);
        foreach (var (game, data) in games.Zip(gameData))
            ReferenceEquals(game, data).Should().BeTrue("both interfaces must share one instance per game");
    }

    [Fact]
    public void AddBepInExGames_ResolutionIsStableAcrossQueries()
    {
        using var provider = Build();

        var first = provider.GetServices<IGame>().ToArray();
        var second = provider.GetServices<IGame>().ToArray();

        foreach (var (a, b) in first.Zip(second))
            ReferenceEquals(a, b).Should().BeTrue("registrations are singletons");
    }

    [Fact]
    public void FamilyGames_ExposeWorkingGameSurface()
    {
        using var provider = Build();
        var games = provider.GetServices<IGame>().Cast<GenericBepInExGame>().ToArray();

        games.Should().AllSatisfy(game =>
        {
            game.StoreIdentifiers.SteamAppIds.Should().NotBeEmpty();
            game.StoreIdentifiers.GameId.Should().Be(game.GameId);
            game.LibraryItemInstallers.Should().HaveCount(2);
            game.DiagnosticEmitters.Should().HaveCount(1);
            game.IconImage.Should().NotBeNull();
            game.TileImage.Should().NotBeNull();
        });

        // The two installers are shared singletons across the whole family.
        var installers = games.SelectMany(g => g.LibraryItemInstallers).Distinct().ToArray();
        installers.Should().HaveCount(2);
    }
}
