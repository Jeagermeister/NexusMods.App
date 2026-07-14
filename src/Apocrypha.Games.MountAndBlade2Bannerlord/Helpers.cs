using System.Runtime.CompilerServices;
using Bannerlord.ModuleManager;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Extensions;
using Apocrypha.Games.MountAndBlade2Bannerlord.Models;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.Resources;

namespace Apocrypha.Games.MountAndBlade2Bannerlord;

public class Helpers
{
    public static async IAsyncEnumerable<ValueTuple<BannerlordModuleLoadoutItem.ReadOnly, ModuleInfoExtended>> GetAllManifestsAsync(
        ILogger logger,
        Loadout.ReadOnly loadout,
        IResourceLoader<BannerlordModuleLoadoutItem.ReadOnly, ModuleInfoExtended> pipeline,
        bool onlyEnabled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerable = LoadoutItem.FindByLoadout(loadout.Db, loadout)
            .OfTypeLoadoutItemGroup()
            .OfTypeBannerlordModuleLoadoutItem();

        foreach (var bannerlordMod in enumerable)
        {
            // The launch path must not inject disabled modules into the game's load order
            // (mirrors the SMAPI helper's onlyEnabled flag); diagnostics pass false so they
            // can still reason over disabled mods.
            if (onlyEnabled && !bannerlordMod.AsLoadoutItemGroup().AsLoadoutItem().IsEnabled())
                continue;

            Resource<ModuleInfoExtended> resource;

            try
            {
                resource = await pipeline.LoadResourceAsync(bannerlordMod, cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Exception while getting manifest for `{Name}`", bannerlordMod.AsLoadoutItemGroup().AsLoadoutItem().Name);
                continue;
            }

            yield return (bannerlordMod, resource.Data);
        }
    }
}
