using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Thunderstore;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.IO;
using Xunit;

namespace Apocrypha.Games.BepInEx.Tests;

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

            // The capability UI surfaces use to discover the game's community (DESIGN-app-layout.md §5).
            game.Should().BeAssignableTo<IThunderstoreCommunityGame>()
                .Which.ThunderstoreCommunitySlug.Should().Be(game.Data.CommunitySlug);
        });

        // The pack installer is one shared singleton; plugin installers are per-game
        // (each carries its game's schema installRules).
        var packInstallers = games.Select(g => g.LibraryItemInstallers[0]).Distinct().ToArray();
        packInstallers.Should().HaveCount(1);
        var pluginInstallers = games.Select(g => g.LibraryItemInstallers[1]).Distinct().ToArray();
        pluginInstallers.Should().HaveCount(games.Length);
    }

    [Fact]
    public void FamilyGames_WireRuntimeArtWhenHttpAndFilesystemAreAvailable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HttpClient());
        services.AddSingleton<IFileSystem>(new InMemoryFileSystem());
        services.AddBepInExGames();
        using var provider = services.BuildServiceProvider();
        var games = provider.GetServices<IGame>().Cast<GenericBepInExGame>().ToArray();

        // Every row in the snapshot carries a cover, so every tile is runtime-fetched.
        games.Should().AllSatisfy(game => game.TileImage.Should().BeOfType<CachedHttpStreamFactory>());

        // Since PR G the RoR2 row is in the family; riskofrain2 is a legacy community with
        // no community block (no 192x192 icon) — its COVER doubles as the icon so the spine
        // and detected grid show real art instead of the placeholder.
        var riskOfRain2 = games.Single(g => g.GameId == GameId.From("RiskOfRain2"));
        ((CachedHttpStreamFactory)riskOfRain2.IconImage).Uri.Should().Be(
            new Uri("https://gcdn.thunderstore.io/assets/riskofrain2/riskofrain2-cover-360x480.webp"));

        var lethalCompany = games.Single(g => g.GameId == GameId.From("LethalCompany"));
        ((CachedHttpStreamFactory)lethalCompany.TileImage).Uri.Should().Be(
            new Uri("https://gcdn.thunderstore.io/assets/lethal-company/lethal-company-cover-360x480.webp"));
        ((CachedHttpStreamFactory)lethalCompany.IconImage).Uri.Should().Be(
            new Uri("https://gcdn.thunderstore.io/assets/lethal-company/lethal-company-icon-192x192.webp"));

        var subnautica = games.Single(g => g.GameId == GameId.From("Subnautica"));
        ((CachedHttpStreamFactory)subnautica.IconImage).Uri.Should().Be(
            new Uri("https://gcdn.thunderstore.io/assets/subnautica/subnautica-cover-360x480.webp"),
            because: "legacy communities carry no icon — the cover doubles as the icon");
    }

    [Fact]
    public void FamilyGames_FallBackToPlaceholderArtInLeanContainers()
    {
        // No HttpClient/IFileSystem registered (headless verbs, lean test hosts).
        using var provider = Build();
        var games = provider.GetServices<IGame>().Cast<GenericBepInExGame>().ToArray();

        games.Should().AllSatisfy(game =>
        {
            game.TileImage.Should().BeOfType<EmbeddedResourceStreamFactory<GenericBepInExGame>>();
            game.IconImage.Should().BeOfType<EmbeddedResourceStreamFactory<GenericBepInExGame>>();
        });
    }
}
