using DynamicData.Kernel;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;

namespace Apocrypha.Games.CreationEngine.SortOrder;

/// <summary>
/// A Creation Engine plugin as shown in the load-order UI.
/// </summary>
/// <remarks>
/// Keys are the plugin file name folded to lower case: the engine treats plugin names as
/// case-insensitive, and the same plugin can legitimately appear with different casing in curated
/// data, the datastore, and on disk (see the NanoSuit.esp/Nanosuit.esp incident). The display name
/// keeps the original casing.
/// </remarks>
public class PluginReactiveSortItem : IReactiveSortItem<PluginReactiveSortItem, SortItemKey<string>>
{
    public PluginReactiveSortItem(int sortIndex, string pluginName, string modName, bool isActive)
    {
        SortIndex = sortIndex;
        DisplayName = pluginName;
        ModName = modName;
        IsActive = isActive;
        Key = MakeKey(pluginName);
    }

    /// <summary>
    /// Canonical sort-item key for a plugin file name.
    /// </summary>
    public static SortItemKey<string> MakeKey(string pluginName) => new(pluginName.ToLowerInvariant());

    public SortItemKey<string> Key { get; }

    public int SortIndex { get; set; }
    public string DisplayName { get; }
    public string ModName { get; set; }
    public Optional<LoadoutItemGroupId> ModGroupId { get; set; }
    public bool IsActive { get; set; }
    public ISortItemLoadoutData? LoadoutData { get; set; }
}

/// <summary>
/// A persisted plugin sort-order entry: the case-folded key plus the display casing to persist.
/// </summary>
public class PluginSortItemData : SortItemData<SortItemKey<string>>
{
    public PluginSortItemData(string pluginName, int sortIndex)
        : base(PluginReactiveSortItem.MakeKey(pluginName), sortIndex)
    {
        PluginName = pluginName;
    }

    /// <summary>
    /// The plugin file name in display casing, as stored in <c>PluginSortOrderItem.PluginName</c>.
    /// </summary>
    public string PluginName { get; }
}

/// <summary>
/// A plugin present in the loadout, before sorting: the case-folded key plus the file name as it
/// appears in the loadout.
/// </summary>
public class PluginSortItemLoadoutData : SortItemLoadoutData<SortItemKey<string>>
{
    public PluginSortItemLoadoutData(string fileName, bool isEnabled, string modName, Optional<LoadoutItemGroupId> modGroupId)
        : base(PluginReactiveSortItem.MakeKey(fileName), isEnabled, modName, modGroupId)
    {
        FileName = fileName;
    }

    /// <summary>
    /// The plugin file name as targeted by the loadout, in original casing.
    /// </summary>
    public string FileName { get; }
}
