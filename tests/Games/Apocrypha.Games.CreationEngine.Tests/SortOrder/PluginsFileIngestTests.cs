using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.Games.CreationEngine.SortOrder;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.StandardGameLocators.TestHelpers.StubbedGames;
using NSubstitute;

namespace Apocrypha.Games.CreationEngine.Tests.SortOrder;

/// <summary>
/// Coverage for learning a plugin order out of a plugins.txt this app did not write — review
/// finding B-1, where <c>PluginsFile.Ingest</c> was a no-op and a hand-edited or pre-existing file
/// was therefore both never learned and never regenerated.
/// </summary>
/// <remarks>
/// Same harness reasoning as <see cref="PluginSortOrderVarietyTests"/>: assembly-level DI on the
/// stubbed game, because <c>AIsolatedGameTest</c> is CI-hostile and the real Creation Engine games
/// do not locate on runners. Plugin names are prefixed per test class on purpose — this suite's
/// datastore is shared across parallel classes.
/// </remarks>
public class PluginsFileIngestTests(IServiceProvider serviceProvider) : AGameTest<StubbedGame>(serviceProvider)
{
    /// <summary>
    /// The point of the fix: an order that exists only in the file becomes the persisted order.
    /// </summary>
    [Fact]
    public async Task AnEditedFileIsLearnedIntoThePersistedOrder()
    {
        var loadout = await CreateLoadout();
        var variety = ServiceProvider.GetRequiredService<PluginSortOrderVariety>();

        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, new RelativePath[]
            {
                "Data/IngestAlpha.esp",
                "Data/IngestBravo.esp",
                "Data/IngestCharlie.esp",
            }, loadout, "Ingest Test Mod");
            await tx.Commit();
        }
        Refresh(ref loadout);

        // Reconcile first, so there is an existing persisted order for the ingest to overrule.
        await variety.ReconcileSortOrder(await variety.GetOrCreateSortOrderFor(loadout.LoadoutId, loadout.LoadoutId));

        var pluginsFile = MakePluginsFile(variety);

        // A hand-edited file, in an order the app would not have produced on its own.
        var contents = """
                       # this file was edited by hand
                       *IngestCharlie.esp
                       *IngestAlpha.esp
                       *IngestBravo.esp
                       """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
        using var tx2 = Connection.BeginTransaction();
        await pluginsFile.Ingest(stream, loadout, new Dictionary<GamePath, SyncNode>(), tx2);

        var persisted = variety.GetPersistedPluginOrder(loadout.LoadoutId, Connection.Db);
        persisted.Should().Equal("IngestCharlie.esp", "IngestAlpha.esp", "IngestBravo.esp");
    }

    /// <summary>
    /// A file with nothing recognisable in it must leave the persisted order untouched rather than
    /// wiping it — an empty or comment-only plugins.txt is not a statement that there is no order.
    /// </summary>
    [Fact]
    public async Task AFileWithNoPluginsLeavesThePersistedOrderAlone()
    {
        var loadout = await CreateLoadout();
        var variety = ServiceProvider.GetRequiredService<PluginSortOrderVariety>();

        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, new RelativePath[] { "Data/EmptyCaseAlpha.esp", "Data/EmptyCaseBravo.esp" }, loadout, "Empty Case Mod");
            await tx.Commit();
        }
        Refresh(ref loadout);

        await variety.ReconcileSortOrder(await variety.GetOrCreateSortOrderFor(loadout.LoadoutId, loadout.LoadoutId));
        var before = variety.GetPersistedPluginOrder(loadout.LoadoutId, Connection.Db);
        before.Should().NotBeEmpty();

        var pluginsFile = MakePluginsFile(variety);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("# nothing but a comment\n\n"));
        using var tx2 = Connection.BeginTransaction();
        await pluginsFile.Ingest(stream, loadout, new Dictionary<GamePath, SyncNode>(), tx2);

        variety.GetPersistedPluginOrder(loadout.LoadoutId, Connection.Db).Should().Equal(before);
    }

    private PluginsFile MakePluginsFile(PluginSortOrderVariety variety)
    {
        // Ingest only needs the variety and the path for its log line; the game is substituted so
        // this stays a datastore test rather than requiring a real Creation Engine install.
        var game = Substitute.For<ICreationEngineGame>();
        game.PluginsFile.Returns(new GamePath(LocationId.Game, "Data/plugins.txt"));
        return new PluginsFile(NullLogger<PluginsFile>.Instance, game, variety);
    }

    // --- parser cases: these files are hand-edited, so the shapes below are what actually turn up

    [Fact]
    public async Task EnabledAndDisabledEntriesBothContributeTheirPosition()
    {
        // The '*' marks a plugin enabled. Enablement lives on loadout items, not here, so a bare
        // line still counts for ordering purposes.
        var order = await Parse("*One.esp\nTwo.esp\n*Three.esm");
        order.Should().Equal("One.esp", "Two.esp", "Three.esm");
    }

    [Fact]
    public async Task CommentsBlankLinesAndSurroundingWhitespaceAreIgnored()
    {
        var order = await Parse("# Generated by something else\r\n\r\n  *One.esp  \r\n\t*Two.esp\r\n");
        order.Should().Equal("One.esp", "Two.esp");
    }

    [Fact]
    public async Task NonPluginLinesAreNotTreatedAsPlugins()
    {
        // A stray word in a hand-edited file must not become a phantom entry in the persisted order.
        var order = await Parse("*One.esp\nreadme.txt\nnonsense\n*Two.esl\n*\n");
        order.Should().Equal("One.esp", "Two.esl");
    }

    [Fact]
    public async Task DuplicatesAreDroppedAndTheFirstCasingIsKept()
    {
        // Persisted plugin names are a display-casing contract elsewhere in the engine, so the
        // casing that survives must be predictable.
        var order = await Parse("*NanoSuit.esp\n*nanosuit.esp\n*Other.esp");
        order.Should().Equal("NanoSuit.esp", "Other.esp");
    }

    private static async Task<List<string>> Parse(string contents)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
        return await PluginsFile.ParseOrder(stream);
    }
}
