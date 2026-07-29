using System.Text;
using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Games.CreationEngine.Tests;

/// <summary>
/// DLCList.txt is the file whose stray entries crash the main menu with an access violation
/// (the +22E5B45 class) -- these lock in the write rules: only known DLC, only if installed,
/// always in declared (release) order, case-insensitively matched.
/// </summary>
public class DlcListFileTests
{
    private static readonly RelativePath[] KnownDlc = ["DLCRobot.esm", "DLCCoast.esm", "DLCNukaWorld.esm"];

    private static DlcListFile MakeFile() => new(new GamePath(LocationId.AppData, "DLCList.txt"), KnownDlc);

    private static SyncNode LoadoutNode(ulong hash) => new()
    {
        Loadout = new SyncNodePart { Hash = Hash.From(hash), Size = Size.Zero, LastModifiedTicks = 0 },
    };

    private static async Task<string[]> WriteAsync(Dictionary<GamePath, SyncNode> syncTree)
    {
        using var ms = new MemoryStream();
        await MakeFile().Write(ms, default, syncTree);
        return Encoding.UTF8.GetString(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd('\r'))
            .ToArray();
    }

    [Fact]
    public async Task WritesOnlyInstalledKnownDlc_InDeclaredOrder()
    {
        var lines = await WriteAsync(new Dictionary<GamePath, SyncNode>
        {
            // Deliberately added out of declared order; a mod plugin and a DLC without a
            // loadout part must both stay out of the file.
            [new GamePath(LocationId.Game, "Data/DLCNukaWorld.esm")] = LoadoutNode(3),
            [new GamePath(LocationId.Game, "Data/DLCRobot.esm")] = LoadoutNode(1),
            [new GamePath(LocationId.Game, "Data/SomeMod.esm")] = LoadoutNode(4),
            [new GamePath(LocationId.Game, "Data/DLCCoast.esm")] = default,
        });

        lines.Should().Equal("DLCRobot.esm", "DLCNukaWorld.esm");
    }

    [Fact]
    public async Task MatchesDlcPathsCaseInsensitively()
    {
        // Windows-authored content routinely re-cases paths; GamePath folds case, and the
        // manifest must still list the DLC under its canonical name.
        var lines = await WriteAsync(new Dictionary<GamePath, SyncNode>
        {
            [new GamePath(LocationId.Game, "data/dlcrobot.esm")] = LoadoutNode(1),
        });

        lines.Should().Equal("DLCRobot.esm");
    }

    [Fact]
    public async Task WritesNothingWhenNoDlcInstalled()
    {
        var lines = await WriteAsync(new Dictionary<GamePath, SyncNode>
        {
            [new GamePath(LocationId.Game, "Data/SomeMod.esp")] = LoadoutNode(9),
        });

        lines.Should().BeEmpty();
    }
}
