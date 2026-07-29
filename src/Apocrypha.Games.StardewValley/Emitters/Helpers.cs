using DynamicData.Kernel;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Diagnostics.Values;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Extensions;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Sdk.Resources;
using Apocrypha.Games.StardewValley.Models;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.NexusModsApi;
using StardewModdingAPI;
using StardewModdingAPI.Toolkit;
using StardewModdingAPI.Toolkit.Serialization.Models;

namespace Apocrypha.Games.StardewValley.Emitters;

internal static class Helpers
{
    /// <summary>
    /// Resolves the mod group that owns a manifest file.
    /// </summary>
    /// <remarks>
    /// An item whose parent has been retracted is a husk: <c>Parent</c> throws on it. Emitters
    /// run over every item in the loadout, so one husk would otherwise abort the entire
    /// diagnostics pass for the game. Callers skip what this returns false for.
    /// </remarks>
    public static bool TryGetOwningGroup(SMAPIManifestLoadoutFile.ReadOnly manifestLoadoutItem, out LoadoutItemGroup.ReadOnly group)
    {
        var loadoutItem = manifestLoadoutItem.AsLoadoutFile().AsLoadoutItemWithTargetPath().AsLoadoutItem();
        if (!loadoutItem.HasParent())
        {
            group = default(LoadoutItemGroup.ReadOnly);
            return false;
        }

        group = loadoutItem.Parent;
        return true;
    }

    public static NamedLink GetNexusModsLink(IGameDomainToGameIdMappingCache mapping) => new("Nexus Mods", NexusModsUrlBuilder.GetGameUri(mapping[StardewValley.NexusModsGameId.Value]));
    public static NamedLink GetSMAPILink(IGameDomainToGameIdMappingCache mapping) => new("Nexus Mods", NexusModsUrlBuilder.GetModUri(mapping[StardewValley.NexusModsGameId.Value], ModId.From(2400)));

    public static ISemanticVersion GetGameVersion(Loadout.ReadOnly loadout)
    {
        var game = loadout.Game;

        // NOTE(erri120): `Major.Minor.Patch` is the only thing the SMAPI API supports
        // in regard to SemanticVersion. Passing a `System.Version` into the constructor
        // will create a `SemanticVersion` with only `Major`, `Minor`, and `Patch` fields.
        // The string parser of `SemanticVersion` accepts more if `allowNonStandard` is enabled,
        // however the SMAPI API will not return any data if a "non-standard" version is passed
        // to it for some reason.
        // See https://github.com/Nexus-Mods/NexusMods.App/pull/2713 for details.
        var localVersion = game
            .GetLocalVersion(loadout.InstallationInstance)
            .Convert(static version => new SemanticVersion(version));

        if (localVersion.HasValue) return localVersion.Value;

        // NOTE(erri120): should only be hit during tests
        var vanityVersion = loadout.GameVersion;
        var rawVersion = vanityVersion.Value;

#if DEBUG
        // NOTE(erri120): dumb hack for tests
        var index = rawVersion.IndexOf(".stubbed", StringComparison.OrdinalIgnoreCase);
        if (index != -1)
        {
            rawVersion = rawVersion.AsSpan()[..index].ToString();
        }
#endif

        var gameVersion = new SemanticVersion(rawVersion, allowNonStandard: true);
        return gameVersion;
    }

    public static bool TryGetSMAPI(Loadout.ReadOnly loadout, out SMAPILoadoutItem.ReadOnly smapi)
    {
        var foundSMAPI = LoadoutItem.FindByLoadout(loadout.Db, loadout)
            .OfTypeLoadoutItemGroup()
            .OfTypeSMAPILoadoutItem()
            .TryGetFirst(x => x.AsLoadoutItemGroup().AsLoadoutItem().IsEnabled(), out smapi);

        return foundSMAPI;
    }

    public static async ValueTask<IReadOnlyList<ValueTuple<SMAPIManifestLoadoutFile.ReadOnly, Manifest>>> GetAllManifestsAsync(
        ILogger logger,
        IDb db,
        LoadoutId loadoutId,
        bool onlyEnabled,
        IResourceLoader<SMAPIManifestLoadoutFile.ReadOnly, Manifest> pipeline,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ValueTuple<SMAPIManifestLoadoutFile.ReadOnly, Manifest>>();
        var manifestLoadoutItems = SMAPIManifestLoadoutFile.GetAllInLoadout(db, loadoutId, onlyEnabled: onlyEnabled);
        foreach (var manifestLoadoutItem in manifestLoadoutItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var manifest = await pipeline.LoadResourceAsync(manifestLoadoutItem, cancellationToken);
                result.Add((manifestLoadoutItem, manifest.Data));
            }
            catch (Exception e)
            {
                // Resolve the name defensively: an unguarded Parent here would throw from inside
                // the handler, turning one unreadable manifest into a failed diagnostics pass.
                var groupName = TryGetOwningGroup(manifestLoadoutItem, out var owningGroup)
                    ? owningGroup.AsLoadoutItem().Name
                    : "<no owning group>";
                logger.LogError(e, "Exception while loading manifest for `{GroupName}`", groupName);
            }
        }

        return result;
    }
}
