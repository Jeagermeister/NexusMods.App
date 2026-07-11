using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.RedEngine.Cyberpunk2077;

public class RunCyberpunk2077Game(IServiceProvider provider, Cyberpunk2077Game game) : RunGameTool<Cyberpunk2077Game>(provider, game)
{
    public override Task Execute(Loadout.ReadOnly loadout, CancellationToken cancellationToken, string[]? commandLineArgs)
    {
        if (commandLineArgs == null)
        {
            commandLineArgs = ["-modded"];
        }
        else
        {
            commandLineArgs = commandLineArgs.Append("-modded").ToArray();
        }
        return base.Execute(loadout, cancellationToken, commandLineArgs);
    }
}
