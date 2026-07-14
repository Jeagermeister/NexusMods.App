using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.GarbageCollection.Interfaces;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.IndexSegments;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.App.GarbageCollection.DataModel;

/// <summary>
/// This class is responsible for finding all used files within the data store
/// and marking them as 'used'.
/// </summary>
public static class DataStoreReferenceMarker
{
    /// <summary>
    /// This method walks through the data store for all <see cref="LoadoutItem"/>(s)
    /// to determine all files used by the data store.
    /// </summary>
    /// <param name="connection">The connection to the MnemonicDB Database.</param>
    /// <param name="archiveGc">The garbage collector from which to reference count all files.</param>
    public static void MarkUsedFiles<TParsedHeaderState, TFileEntryWrapper>(IConnection connection, ArchiveGarbageCollector<TParsedHeaderState, TFileEntryWrapper> archiveGc, ILogger? logger = null)
        where TParsedHeaderState : ICanProvideFileHashes<TFileEntryWrapper>
        where TFileEntryWrapper : IHaveFileHash
    {
        MarkUsedFiles(connection, hash => archiveGc.AddReferencedFile(hash), logger);
    }

    /// <summary>
    /// Same walk as <see cref="MarkUsedFiles{TParsedHeaderState,TFileEntryWrapper}(IConnection,ArchiveGarbageCollector{TParsedHeaderState,TFileEntryWrapper},ILogger?)"/>,
    /// reporting each referenced hash to <paramref name="addReferencedFile"/>. Reads a FRESH
    /// <see cref="IConnection.Db"/> snapshot at call time — used by the pre-delete re-check in
    /// <see cref="RunGarbageCollector"/> to close the mark→delete TOCTOU window.
    /// </summary>
    public static void MarkUsedFiles(IConnection connection, Action<Hash> addReferencedFile, ILogger? logger = null)
    {
        var db = connection.Db;
        var loadoutFiles = LoadoutFile.All(db);
        var isLoadoutValidDict = new Dictionary<LoadoutId, bool>();
        
        // Loadouts will have items like 'Game Files', these do not have a corresponding
        // library item.
        MarkItemsUsedInLoadouts(addReferencedFile, loadoutFiles, db, isLoadoutValidDict, logger);

        // Library will have all of our mods and other things installed from the outside.
        MarkItemsUsedInLibrary(addReferencedFile, db);
        
        // Mark non Loadout/Library items that are backups (e.g. Backups of original game files)
        MarkItemsMarkedAsBackups(addReferencedFile, db);
    }

    private static void MarkItemsMarkedAsBackups(Action<Hash> addReferencedFile, IDb db)
    {
        // All files explicitly marked as roots should be preserved.
        var gameBackedUpFile = GameBackedUpFile.All(db);
        foreach (var file in gameBackedUpFile)
            addReferencedFile(file.Hash);
    }

    private static void MarkItemsUsedInLibrary(Action<Hash> addReferencedFile, IDb db)
    {
        /*
            Note(sewer)

            How the whole system is built is not particularly easy to understand
            at first, so here's a short explainer that's also in the docs.

            This is based on reading the implementation of AddLibraryFileJobWorker 
            (which is recursive across a base type, and thus not trivial to follow).

            Essentially, we want to scan for all cases of `LibraryArchiveFileEntry` here.
            These belong to an `LibraryArchive`; which is the primitive you usually
            interact with in the UI to add items. Both are of type `LibraryFile`.

            Therefore, we need to scan for all non-retracted `LibraryArchiveFileEntry`
            items.
         */

        var libraryFiles = LibraryArchiveFileEntry.All(db);
        foreach (var file in libraryFiles)
            addReferencedFile(file.AsLibraryFile().Hash);

        /*
            There is however a caveat that has to be considered here.
            
            There is a base assumption that all `LibraryArchiveFileEntry` items
            are used to represent items that are archived in the File Store.
            
            If this assumption is broken, then the GC will miss files.
            An appropriate warning has been added to the relevant classes.
        */
    }

    private static void MarkItemsUsedInLoadouts(Action<Hash> addReferencedFile, Entities<LoadoutFile.ReadOnly> loadoutFiles, IDb db, Dictionary<LoadoutId, bool> isLoadoutValidDict, ILogger? logger)
    {
        foreach (var loadoutFile in loadoutFiles)
        {
            // TODO: Implement recursive includes, e.g. AsLoadoutItemWithTargetPath + AsLoadoutItem
            // into a single method.
            var loadoutItem = new LoadoutItem.ReadOnly(db, loadoutFile.Id);
            if (!isLoadoutValidDict.TryGetValue(loadoutItem.LoadoutId, out var isLoadoutValid))
            {
                var loadout = loadoutItem.Loadout;
                isLoadoutValid = loadout.IsValid();
                isLoadoutValidDict[loadoutItem.LoadoutId] = isLoadoutValid;

                // FAIL-SAFE (CODE_REVIEW.md §7 #21): a live file whose loadout row is invalid is a
                // data inconsistency (e.g. a crash-orphaned loadout), not proof the file is garbage.
                // Deleting on inconsistency is fail-open and permanent; keep the file and let
                // `loadouts delete-orphaned` retract the items properly, after which the next GC
                // collects them.
                if (!isLoadoutValid)
                    logger?.LogWarning("GC: loadout {LoadoutId} is invalid but still has live files; keeping its files (run `loadouts delete-orphaned` to clean up)", loadoutItem.LoadoutId);
            }

            addReferencedFile(loadoutFile.Hash);
        }
    }
}
