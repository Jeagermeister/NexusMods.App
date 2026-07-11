using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.WorkspaceSystem;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.App.UI.WorkspaceAttachments;

public class LoadoutAttachmentsFactory(IConnection conn) : IWorkspaceAttachmentsFactory<LoadoutContext>
{
    public string CreateTitle(LoadoutContext context)
    {
        // Use the game name as the title. Orphaned loadouts (game uninstalled/moved) must not
        // throw here — this runs during saved-window-state restoration.
        var loadout = Loadout.Load(conn.Db, context.LoadoutId);
        var gameRegistry = conn.ServiceProvider.GetRequiredService<Apocrypha.Sdk.Games.IGameRegistry>();
        if (!gameRegistry.TryGetGameInstallation(loadout, out var installation)) return loadout.Name;
        return installation!.Game.DisplayName;
    }

    public string CreateSubtitle(LoadoutContext context)
    {
        // Use the loadout name as the subtitle
        var loadout = Loadout.Load(conn.Db, context.LoadoutId);
        return loadout.Name;
    }
}
