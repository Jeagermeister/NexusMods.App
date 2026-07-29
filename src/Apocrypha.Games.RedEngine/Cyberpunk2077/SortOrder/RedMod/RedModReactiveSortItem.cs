using DynamicData.Kernel;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using NexusMods.Paths;

namespace Apocrypha.Games.RedEngine.Cyberpunk2077.SortOrder;

public class RedModReactiveSortItem : IReactiveSortItem<RedModReactiveSortItem, SortItemKey<string>>
{
    public RedModReactiveSortItem(int sortIndex, RelativePath redModFolderName, string modName, bool isActive)
    {
        SortIndex = sortIndex;
        RedModFolderName = redModFolderName;
        DisplayName = redModFolderName.ToString();
        ModName = modName;
        IsActive = isActive;
        Key = new SortItemKey<string>(redModFolderName);
    }

    /// <summary>
    /// Case-folding comparison key for persistence matching: the same REDmod folder can
    /// legitimately appear with different casing in the datastore and a reinstalled archive
    /// (the NanoSuit.esp incident class), and a case-variant miss in PersistSortOrderCore
    /// silently resets the mod's position. Reactive keys deliberately keep display casing —
    /// MoveItems and modlist.txt are keyed/written display-cased; folding those end-to-end
    /// needs the Creation Engine's PluginSortItemData pattern (folded key + display field).
    /// </summary>
    public static SortItemKey<string> MakeKey(RelativePath redModFolderName) => new(redModFolderName.ToString().ToLowerInvariant());
    
    public RelativePath RedModFolderName { get; set; }

    public SortItemKey<string> Key { get; }

    public int SortIndex { get; set; }
    public string DisplayName { get; }
    public string ModName { get; set; }
    public Optional<LoadoutItemGroupId> ModGroupId { get; set; }
    public bool IsActive { get; set; }
    public ISortItemLoadoutData? LoadoutData { get; set; }
}
