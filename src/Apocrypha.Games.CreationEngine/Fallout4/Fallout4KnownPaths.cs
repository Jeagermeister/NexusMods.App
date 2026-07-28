using NexusMods.Paths;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Games.CreationEngine.Fallout4;

public class Fallout4KnownPaths
{
    public static readonly GamePath PluginsFile = new (LocationId.AppData, "plugins.txt");

    /// <summary>
    /// The DLC manifest the main menu walks to build its DLC banners. Lives beside
    /// <see cref="PluginsFile"/>; see <see cref="DlcListFile"/> for why it has to be managed.
    /// </summary>
    public static readonly GamePath DlcListFile = new (LocationId.AppData, "DLCList.txt");

    /// <summary>
    /// The six official Fallout 4 DLC, in release order — which is also the order the vanilla
    /// launcher writes them and the order their banners are expected in.
    /// </summary>
    public static readonly IReadOnlyList<RelativePath> Dlc =
    [
        "DLCRobot.esm",
        "DLCworkshop01.esm",
        "DLCCoast.esm",
        "DLCworkshop02.esm",
        "DLCworkshop03.esm",
        "DLCNukaWorld.esm",
    ];
}
