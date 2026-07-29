using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.CreationEngine.Emitters;

/// <summary>
/// Warns when applying the loadout would add or remove plugins while saves already exist.
/// </summary>
/// <remarks>
/// A Creation Engine save records the plugins it was written with and refers to their contents by
/// load order index. Inserting or dropping a plugin renumbers everything after it, so an existing
/// save resolves its form IDs against the wrong plugins — the game refuses the save or crashes
/// shortly after loading it. This is easy to trigger by accident: repairing a broken mod install is
/// a strictly good change to the loadout that is nonetheless fatal to a save made before it.
/// </remarks>
public class SaveBreakingChangeEmitter : ILoadoutDiagnosticEmitter
{
    /// <summary>Names listed individually before the message falls back to a bare count.</summary>
    private const int MaxNamesListed = 10;

    private static readonly GamePath SavesPath = new(LocationId.Preferences, "Saves");

    public async IAsyncEnumerable<Diagnostic> Diagnose(Loadout.ReadOnly loadout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Obsolete overload -- the syncTree overload below is the real implementation.
        await Task.Yield();
        yield break;
    }

    public async IAsyncEnumerable<Diagnostic> Diagnose(Loadout.ReadOnly loadout, FrozenDictionary<GamePath, SyncNode> syncTree, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        var saveCount = CountSaves(loadout);
        if (saveCount == 0)
            yield break;

        List<string> added = [];
        List<string> removed = [];

        foreach (var (path, node) in syncTree)
        {
            if (path.LocationId != LocationId.Game ||
                path.Parent != KnownPaths.Data ||
                !KnownCEExtensions.PluginFiles.Contains(path.Extension))
                continue;

            // Only a plugin appearing or disappearing renumbers the load order. A plugin whose
            // contents changed keeps its position, so saves survive it.
            if (node.HaveLoadout && !node.HaveDisk)
                added.Add(path.FileName);
            else if (node.HaveDisk && !node.HaveLoadout)
                removed.Add(path.FileName);
        }

        if (added.Count == 0 && removed.Count == 0)
            yield break;

        yield return Diagnostics.CreateSaveBreakingLoadOrderChange(
            saveCount,
            added.Count,
            removed.Count,
            Describe(added),
            Describe(removed)
        );
    }

    private static string Describe(List<string> names)
    {
        if (names.Count == 0)
            return "none";

        names.Sort(StringComparer.OrdinalIgnoreCase);

        return names.Count <= MaxNamesListed
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxNamesListed))} and {names.Count - MaxNamesListed} more";
    }

    private static int CountSaves(Loadout.ReadOnly loadout)
    {
        try
        {
            var savesDirectory = loadout.InstallationInstance.Locations.ToAbsolutePath(SavesPath);
            if (!savesDirectory.DirectoryExists())
                return 0;

            return savesDirectory.EnumerateFiles("*", false).Count();
        }
        catch (Exception)
        {
            // The saves folder is outside anything we manage, so it may be missing, unreadable, or
            // on a location the installation does not define. A diagnostic must never be the thing
            // that breaks an apply.
            return 0;
        }
    }
}
