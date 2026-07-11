using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Sdk.Library;

namespace Apocrypha.App.UI.Pages.Library;

/// <summary>
///     Finds redundant copies of downloaded files in the Library. Duplicates happen when the
///     same download is triggered more than once concurrently (e.g. double-clicked one-click
///     install links) — the copies are byte-identical, so removal loses nothing.
/// </summary>
public static class LibraryDuplicateFinder
{
    /// <summary>
    ///     Returns the library items that can be safely removed: for every group of library
    ///     files with identical content (hash + size), all copies except one. Copies linked
    ///     into a loadout are always kept (removing them would alter the loadout); if no copy
    ///     is linked, the oldest one is kept. Collection source archives are never touched —
    ///     collections resolve their archive by hash, so "duplicates" of those are load-bearing.
    /// </summary>
    public static LibraryItem.ReadOnly[] FindRemovableDuplicates(IDb db)
    {
        var toRemove = new List<LibraryItem.ReadOnly>();

        var groups = LibraryFile.All(db)
            // Files INSIDE downloaded archives are LibraryFiles too — identical nested files
            // (e.g. the icon.png every Thunderstore package ships) are not duplicates of
            // anything the user downloaded. Only top-level downloads count.
            .Where(file => !file.TryGetAsLibraryArchiveFileEntry(out _))
            .Where(file => !file.TryGetAsNexusModsCollectionLibraryFile(out _))
            .GroupBy(file => (file.Hash, file.Size));

        foreach (var group in groups)
        {
            var copies = group.OrderBy(file => file.Id).ToArray();
            if (copies.Length < 2) continue;

            var unlinked = copies
                .Where(copy => !LibraryLinkedLoadoutItem.FindByLibraryItem(db, copy.Id).Any())
                .ToArray();

            var keepOneUnlinked = unlinked.Length == copies.Length;
            toRemove.AddRange(
                (keepOneUnlinked ? unlinked.Skip(1) : unlinked)
                .Select(file => file.AsLibraryItem())
            );
        }

        return toRemove.ToArray();
    }
}
