using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.Tests;

public class GameFileStreamSourceTests : ACyberpunkIsolatedGameTest<GameFileStreamSourceTests>
{
    private readonly GameFileStreamSource _source;

    public GameFileStreamSourceTests(ITestOutputHelper helper) : base(helper)
    {
        _source = ServiceProvider.GetServices<IReadOnlyStreamSource>().OfType<GameFileStreamSource>().Single();
    }

    [Fact]
    public async Task ResolveFindsSyncedGameFileByHashWithoutThrowing()
    {
        var loadoutA = await CreateLoadout();
        loadoutA = await Synchronizer.Synchronize(loadoutA);

        var pathToTest = new GamePath(LocationId.Game, "bin/resolveTestFile.txt");
        var resolvedDiskPath = GameInstallation.Locations.ToAbsolutePath(pathToTest);
        resolvedDiskPath.Parent.CreateDirectory();
        await resolvedDiskPath.WriteAllTextAsync("Hello Resolve!");

        loadoutA = await Synchronizer.Synchronize(loadoutA);

        var diskEntry = DiskStateEntry.FindByGame(loadoutA.Installation.Db, loadoutA.Installation)
            .First(f => f.Path.Item2 == pathToTest.LocationId && f.Path.Item3 == pathToTest.Path);

        // Regression test: GameFileStreamSource.Resolve used to select Path.Item1/Item2/Item3
        // (3 columns from the GamePathParentAttribute's (EntityId, LocationId, RelativePath)
        // tuple) alongside a separate Game column, giving 7 selected columns for the 6-field
        // target row type. The extra EntityId column shifted every later field by one position,
        // so LocationId read the EntityId column, throwing "No value adaptor found for
        // {LocationId} from DuckDB type {UBigInt}" whenever this codepath was hit, e.g.
        // installing a collection whose files overlap ones already on disk.
        var act = () => _source.Resolve(diskEntry.Hash);
        act.Should().NotThrow();

        act().Should().Be(resolvedDiskPath);
    }
}
