using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.Thunderstore.Models;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.App.UI.Pages.Library;

/// <summary>
///     Represents the properties needed for deletion of a library item.
/// </summary>
/// <param name="IsNexus">Whether the library item came from a redownloadable mod source (Nexus Mods or Thunderstore).</param>
/// <param name="IsNonPermanent">
///     Whether the library item is a download that is not guaranteed to be redownloadable.
///     (As of time of writing it means 'not from a known mod source')
/// </param>
/// <param name="IsManuallyAdded">Whether this library item was manually added from FileSystem to library.</param>
/// <param name="Loadouts">The loadouts that this library item is used within.</param>
public record struct LibraryItemRemovalInfo(bool IsNexus, bool IsNonPermanent, bool IsManuallyAdded, Loadout.ReadOnly[] Loadouts)
{
    public static LibraryItemRemovalInfo Determine(LibraryItem.ReadOnly toRemove, Loadout.ReadOnly[] loadouts)
    {
        var info = new LibraryItemRemovalInfo();

        // Check if it's a file which was downloaded from a redownloadable mod source.
        if (toRemove.TryGetAsNexusModsLibraryItem(out _) || toRemove.TryGetAsThunderstoreLibraryItem(out _))
        {
            info.IsNexus = true;
            info.IsNonPermanent = !info.IsNexus;
        }
        else if (toRemove.TryGetAsLocalFile(out _))
        {
            info.IsManuallyAdded = true;
        }
        
        // Check if it's added to any loadout
        info.Loadouts = loadouts.Where(loadout => GetLoadoutItemsByLibraryItem(loadout, toRemove).Any()).ToArray();
        return info;
    }

    /// <summary>
    /// Returns an enumerable containing all loadout items linked to the given library item.
    /// </summary>
    private static IEnumerable<LibraryLinkedLoadoutItem.ReadOnly> GetLoadoutItemsByLibraryItem(Loadout.ReadOnly loadout, LibraryItem.ReadOnly libraryItem)
    {
        // Start with a backref. This assumes that the number of loadouts with a given library item will be fairly small.
        // This could be false, but it's a good starting point.
        return LibraryLinkedLoadoutItem
            .FindByLibraryItem(loadout.Db, libraryItem)
            .Where(linked => linked.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId == loadout);
    }
}
