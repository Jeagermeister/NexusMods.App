using Apocrypha.Abstractions.Collections.Json;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Abstractions.Collections;

/// <summary>
/// Seeds a game's load order from a collection's curated ordering data after the collection is
/// installed. Collections carry the curator's intended order (e.g. Gamebryo's <c>plugins</c> +
/// <c>pluginRules</c>), which the game module knows how to translate into its persisted sort
/// order; games without curated ordering data simply have no seeder registered.
/// </summary>
public interface ICollectionLoadOrderSeeder
{
    /// <summary>
    /// The games this seeder applies to.
    /// </summary>
    IReadOnlyList<GameId> GameIds { get; }

    /// <summary>
    /// Translates the collection's curated ordering data into the game's persisted sort orders for
    /// the given loadout and collection group. A collection without curated ordering data is a
    /// no-op. Never throws for malformed curator data — degraded input is logged and skipped.
    /// </summary>
    ValueTask SeedAsync(CollectionRoot root, LoadoutId loadoutId, CollectionGroupId collectionGroupId, CancellationToken token = default);
}
