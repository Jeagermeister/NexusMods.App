using System.Runtime.CompilerServices;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Rules;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.FileHashes.Emitters;

public class NoWayToSourceFilesOnDisk : ILoadoutDiagnosticEmitter
{
    public async IAsyncEnumerable<Diagnostic> Diagnose(Loadout.ReadOnly loadout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: Enable this diagnostic once users have a way to back up the game files from UI
        yield break;
#pragma warning disable CS0162 // the code below is kept for when the diagnostic is re-enabled
        
        var syncronizer = loadout.InstallationInstance.GetGame().Synchronizer;
        var syncTree = await syncronizer.BuildSyncTree(loadout);
        syncronizer.ProcessSyncTree(syncTree);
        
        var totalSize = Size.Zero;
        var count = 0;
        
        foreach (var (_, node) in syncTree)
        {
            if (node.Loadout.Hash == node.Disk.Hash && node.SourceItemType == LoadoutSourceItemType.Game && !node.Signature.HasFlag(Signature.DiskArchived))
            {
                totalSize += node.Loadout.Size;
                count++;
            }
        }

        if (count > 0)
        {
            yield return Diagnostics.CreateGameFilesDoNotHaveSource(totalSize, count);
        }
#pragma warning restore CS0162
    }
}
