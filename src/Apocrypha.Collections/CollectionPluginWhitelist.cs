using Apocrypha.Abstractions.Collections.Json;
using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Collections;

/// <summary>
/// Shared logic for enforcing a collection curator's plugin whitelist. Used by the install
/// pipeline (<see cref="InstallCollectionJob"/>) and the `collection-repair-plugin-state`
/// verb so the two can never drift apart.
/// </summary>
internal static class CollectionPluginWhitelist
{
    /// <summary>
    /// Plugin-file extensions the curated whitelist pass applies to.
    /// </summary>
    /// <remarks>
    /// Keep in sync with the Creation Engine module's <c>KnownCEExtensions.PluginFiles</c>;
    /// the projects deliberately do not reference each other, so the list exists in both.
    /// </remarks>
    internal static readonly Extension[] KnownPluginExtensions = [new(".esp"), new(".esm"), new(".esl")];

    /// <summary>
    /// The set of plugin names the curator actually runs. The manifest's `plugins` array is
    /// the enabled set -- Vortex records what the curator loads, not what they disabled.
    /// </summary>
    internal static HashSet<string> CuratorEnabledPlugins(CollectionRoot root)
        => root.Plugins
            .Where(static plugin => plugin.Enabled != false)
            .Select(static plugin => plugin.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Walks <see cref="LoadoutItem.Parent"/> links to check whether <paramref name="item"/>
    /// sits anywhere under <paramref name="ancestorId"/>.
    /// </summary>
    internal static bool HasAncestor(LoadoutItem.ReadOnly item, EntityId ancestorId)
    {
        var current = item;
        while (current.Contains(LoadoutItem.Parent))
        {
            if (current.ParentId.Value == ancestorId) return true;
            current = current.Parent.AsLoadoutItem();
        }
        return false;
    }

    /// <summary>
    /// Adds a Disabled datom for every enabled plugin under the collection group whose name
    /// the curator does not run. Returns the number of plugins disabled; the caller owns the
    /// transaction and decides whether to commit.
    /// </summary>
    internal static int DisableExtraPlugins(
        IDb db,
        ITransaction tx,
        HashSet<string> curatorPlugins,
        LoadoutId loadout,
        EntityId collectionGroupId)
    {
        var disabled = 0;
        foreach (var item in LoadoutItem.FindByLoadout(db, loadout))
        {
            if (!LoadoutItemWithTargetPath.TargetPath.TryGetValue(item, out var rawTargetPath)) continue;

            GamePath targetPath = rawTargetPath;
            if (!KnownPluginExtensions.Contains(targetPath.Path.Extension)) continue;
            if (curatorPlugins.Contains(targetPath.Path.FileName.ToString())) continue;
            if (!HasAncestor(item, collectionGroupId)) continue;
            if (item.Contains(LoadoutItem.Disabled)) continue;

            tx.Add(item.Id, LoadoutItem.Disabled, Null.Instance);
            disabled++;
        }

        return disabled;
    }
}
