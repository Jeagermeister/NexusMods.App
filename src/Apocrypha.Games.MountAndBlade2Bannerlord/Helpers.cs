using System.Runtime.CompilerServices;
using Bannerlord.ModuleManager;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Loadouts;
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
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerable = LoadoutItem.FindByLoadout(loadout.Db, loadout)
            .OfTypeLoadoutItemGroup()
            .OfTypeBannerlordModuleLoadoutItem();

        foreach (var bannerlordMod in enumerable)
        {
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
