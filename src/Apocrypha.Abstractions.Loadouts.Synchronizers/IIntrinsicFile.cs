using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Abstractions.Loadouts.Synchronizers;

public interface IIntrinsicFile
{
    /// <summary>
    /// The game path of this file.
    /// </summary>
    public GamePath Path { get; }

    /// <summary>
    /// Write the contents of this file to the stream.
    /// </summary>
    public Task Write(Stream stream, Loadout.ReadOnly loadout, Dictionary<GamePath, SyncNode> syncTree);

    /// <summary>
    /// Ingest the contents of the stream into the loadout
    /// </summary>
    public Task Ingest(Stream stream, Loadout.ReadOnly loadout, Dictionary<GamePath, SyncNode> syncTree, ITransaction tx);
}
