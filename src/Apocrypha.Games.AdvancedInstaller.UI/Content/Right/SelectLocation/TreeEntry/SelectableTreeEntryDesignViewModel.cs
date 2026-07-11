
using Apocrypha.Sdk.Games;

namespace Apocrypha.Games.AdvancedInstaller.UI.SelectLocation;

public class SelectableTreeEntryDesignViewModel() : SelectableTreeEntryViewModel(
    new GamePath(LocationId.Game, ""),
    SelectableDirectoryNodeStatus.Regular);
