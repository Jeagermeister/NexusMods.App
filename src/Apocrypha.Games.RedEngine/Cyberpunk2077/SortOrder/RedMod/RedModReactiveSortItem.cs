using DynamicData.Kernel;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using NexusMods.Paths;

namespace Apocrypha.Games.RedEngine.Cyberpunk2077.SortOrder;

/// <summary>
/// A REDmod as shown in the load-order UI.
/// </summary>
/// <remarks>
/// Keys are the REDmod folder name folded to lower case: the same folder can legitimately appear
/// with different casing in the datastore and a reinstalled archive (the NanoSuit.esp incident
/// class), and a case-variant miss in any key join silently resets or ignores the mod. Display
/// casing lives in <see cref="RedModFolderName"/> — modlist.txt and the persisted
/// <c>RedModSortOrderItem.RedModFolderName</c> are display-casing contracts and must never be
/// written from a key. This mirrors the Creation Engine's <c>PluginSortItemData</c> pattern.
/// </remarks>
public class RedModReactiveSortItem : IReactiveSortItem<RedModReactiveSortItem, SortItemKey<string>>
{
    public RedModReactiveSortItem(int sortIndex, RelativePath redModFolderName, string modName, bool isActive)
    {
        SortIndex = sortIndex;
        RedModFolderName = redModFolderName;
        DisplayName = redModFolderName.ToString();
        ModName = modName;
        IsActive = isActive;
        Key = MakeKey(redModFolderName);
    }

    /// <summary>
    /// Canonical case-folded sort-item key for a REDmod folder name.
    /// </summary>
    public static SortItemKey<string> MakeKey(RelativePath redModFolderName) => new(redModFolderName.ToString().ToLowerInvariant());

    /// <summary>
    /// The REDmod folder name in display casing — the value modlist.txt and persistence write.
    /// </summary>
    public RelativePath RedModFolderName { get; set; }

    public SortItemKey<string> Key { get; }

    public int SortIndex { get; set; }
    public string DisplayName { get; }
    public string ModName { get; set; }
    public Optional<LoadoutItemGroupId> ModGroupId { get; set; }
    public bool IsActive { get; set; }
    public ISortItemLoadoutData? LoadoutData { get; set; }
}

/// <summary>
/// A persisted REDmod sort-order entry: the case-folded key plus the display casing to persist.
/// </summary>
public class RedModSortItemData : SortItemData<SortItemKey<string>>
{
    public RedModSortItemData(RelativePath redModFolderName, int sortIndex)
        : base(RedModReactiveSortItem.MakeKey(redModFolderName), sortIndex)
    {
        RedModFolderName = redModFolderName;
    }

    /// <summary>
    /// The REDmod folder name in display casing, as stored in
    /// <c>RedModSortOrderItem.RedModFolderName</c>.
    /// </summary>
    public RelativePath RedModFolderName { get; }
}

/// <summary>
/// A REDmod present in the loadout, before sorting: the case-folded key plus the folder name as
/// deployed by the loadout.
/// </summary>
public class RedModSortItemLoadoutData : SortItemLoadoutData<SortItemKey<string>>
{
    public RedModSortItemLoadoutData(RelativePath redModFolderName, bool isEnabled, string modName, Optional<LoadoutItemGroupId> modGroupId)
        : base(RedModReactiveSortItem.MakeKey(redModFolderName), isEnabled, modName, modGroupId)
    {
        RedModFolderName = redModFolderName;
    }

    /// <summary>
    /// The REDmod folder name as targeted by the loadout, in original casing.
    /// </summary>
    public RelativePath RedModFolderName { get; }
}
