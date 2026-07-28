using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.CreationEngine;

/// <summary>
/// Writes the engine's DLC manifest (<c>DLCList.txt</c>).
/// </summary>
/// <remarks>
/// <para>
/// This file sits beside <c>plugins.txt</c> in the same folder and, unlike it, is not a load order —
/// it is the list the main menu walks to build its DLC banners. An entry that is not an official DLC
/// sends that walk looking for a banner texture that does not exist, and the game dies with an
/// access violation on the main menu before the player can touch anything. It is unrecoverable from
/// inside the game and produces no diagnostic, because nothing in the app was managing the file.
/// </para>
/// <para>
/// Writing it ourselves keeps it authoritative: exactly the official DLC the installation actually
/// has, and nothing a mod or an external tool may have appended.
/// </para>
/// </remarks>
public class DlcListFile : IIntrinsicFile
{
    private readonly IReadOnlyList<RelativePath> _knownDlc;

    public DlcListFile(GamePath path, IReadOnlyList<RelativePath> knownDlc)
    {
        Path = path;
        _knownDlc = knownDlc;
    }

    public GamePath Path { get; }

    public async Task Write(Stream stream, Loadout.ReadOnly loadout, Dictionary<GamePath, SyncNode> syncTree)
    {
        await using var sw = new StreamWriter(stream, leaveOpen: true);

        // Declared order is the engine's own release order, which is the order the vanilla launcher
        // writes and the order the banners are expected in.
        foreach (var dlc in _knownDlc)
        {
            var path = new GamePath(LocationId.Game, KnownPaths.Data.Path / dlc);
            if (!syncTree.TryGetValue(path, out var node) || !node.HaveLoadout)
                continue;

            await sw.WriteLineAsync(dlc.ToString());
        }

        await sw.FlushAsync();
    }

    public Task Ingest(Stream stream, Loadout.ReadOnly loadout, Dictionary<GamePath, SyncNode> syncTree, ITransaction tx)
    {
        // Nothing to learn from the file: its contents are fully derived from which official DLC are
        // installed, and anything else in there is exactly what we are trying to remove.
        return Task.CompletedTask;
    }
}
