using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.NexusModsApi;
using NexusMods.MnemonicDB.Abstractions;

namespace Apocrypha.App.UI.Pages.LoadoutPage;

/// <summary>
/// Resolves the required mods that should be turned on alongside a mod the user is enabling.
///
/// When a mod is enabled we look up its Nexus "required mods" (persisted as
/// <see cref="NexusModsModRequirement"/>) and, for any required mod that is <b>already installed
/// in the same loadout but currently disabled</b>, we enable it too. Resolution is transitive
/// through installed mods (A→B→C), but never adds or downloads a mod that isn't present — a
/// missing requirement is left to the diagnostics system.
/// </summary>
internal static class RequiredDependencyEnabler
{
    /// <summary>
    /// Given the loadout items that are about to be enabled, returns the ids of the
    /// currently-disabled loadout items that are (transitively) required by them and already
    /// present in the same loadout. The caller should retract <see cref="LoadoutItem.Disabled"/>
    /// on each returned id, ideally in the same transaction that enables the seed items.
    /// </summary>
    public static IReadOnlyList<EntityId> GetDependenciesToEnable(IDb db, IReadOnlyList<LoadoutItem.ReadOnly> itemsBeingEnabled)
    {
        if (itemsBeingEnabled.Count == 0) return [];

        // The seed items should share a loadout, but group defensively so a mixed selection is safe.
        var result = new List<EntityId>();
        var seen = new HashSet<EntityId>(itemsBeingEnabled.Select(item => item.Id));

        foreach (var loadoutGroup in itemsBeingEnabled.GroupBy(item => item.LoadoutId))
        {
            ResolveForLoadout(db, loadoutGroup.Key, loadoutGroup.ToArray(), result, seen);
        }

        return result;
    }

    private static void ResolveForLoadout(
        IDb db,
        LoadoutId loadoutId,
        IReadOnlyList<LoadoutItem.ReadOnly> seedItems,
        List<EntityId> result,
        HashSet<EntityId> seen)
    {
        // Map every Nexus-sourced mod in the loadout by its mod-page uid, so a required uid can be
        // matched back to the concrete loadout item(s) that provide it.
        var itemsByUid = new Dictionary<ModUid, List<EntityId>>();
        var disabled = new HashSet<EntityId>();

        foreach (var datom in db.Datoms(LoadoutItem.Loadout, loadoutId))
        {
            var itemId = datom.E;
            if (!TryGetModUid(db, itemId, out var uid)) continue;

            if (!itemsByUid.TryGetValue(uid, out var list))
            {
                list = [];
                itemsByUid[uid] = list;
            }
            list.Add(itemId);

            if (LoadoutItem.Load(db, itemId).IsDisabled) disabled.Add(itemId);
        }

        // Breadth-first over the requirement graph, following only edges into installed mods.
        var visited = new HashSet<ModUid>();
        var queue = new Queue<ModUid>();

        foreach (var seed in seedItems)
        {
            if (TryGetModUid(db, seed.Id, out var seedUid) && visited.Add(seedUid))
                queue.Enqueue(seedUid);
        }

        while (queue.Count > 0)
        {
            var ownerUid = queue.Dequeue();

            foreach (var requirementDatom in db.Datoms(NexusModsModRequirement.OwnerUid, ownerUid))
            {
                var requiredUid = NexusModsModRequirement.Load(db, requirementDatom.E).RequiredUid;

                // Only follow the chain through mods that are actually installed.
                if (!itemsByUid.TryGetValue(requiredUid, out var providerIds)) continue;

                foreach (var providerId in providerIds)
                {
                    if (disabled.Contains(providerId) && seen.Add(providerId))
                        result.Add(providerId);
                }

                if (visited.Add(requiredUid)) queue.Enqueue(requiredUid);
            }
        }
    }

    /// <summary>
    /// Walks a loadout item to the Nexus mod-page uid that produced it, if any.
    /// </summary>
    private static bool TryGetModUid(IDb db, EntityId loadoutItemId, out ModUid uid)
    {
        uid = default;

        var linkedItem = LibraryLinkedLoadoutItem.Load(db, loadoutItemId);
        if (!linkedItem.IsValid()) return false;
        if (!linkedItem.LibraryItem.TryGetAsNexusModsLibraryItem(out var nexusLibraryItem)) return false;
        if (!nexusLibraryItem.IsValid()) return false;

        var modPage = nexusLibraryItem.ModPageMetadata;
        if (!modPage.IsValid()) return false;

        uid = modPage.Uid;
        return true;
    }
}
