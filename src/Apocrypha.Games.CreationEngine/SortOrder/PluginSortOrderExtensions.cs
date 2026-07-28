using DynamicData;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.CreationEngine.Models;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.CreationEngine.SortOrder;

public static class PluginSortOrderExtensions
{
    public static PluginSortOrderItem.ReadOnly[] RetrievePluginSortableEntries(this IDb db, SortOrderId sortOrderId)
    {
        return db.Datoms(SortOrderItem.ParentSortOrder, sortOrderId)
            .Select(d => PluginSortOrderItem.Load(db, d.E))
            .Where(si => si.IsValid())
            .OrderBy(si => si.AsSortOrderItem().SortIndex)
            .ToArray();
    }

    public static IReadOnlyList<PluginSortItemData> RetrievePluginSortOrderItems(IDb db, SortOrderId sortOrderId)
    {
        return db.Connection.Query<(string PluginName, int SortIndex, EntityId ItemId)>($"""
            SELECT * FROM cengine.PluginSortOrderItems({db}, {sortOrderId.Value})
            """)
            .Select(row => new PluginSortItemData(row.PluginName, row.SortIndex))
            .ToList();
    }

    public static IEnumerable<(string PluginFileName, bool IsEnabled, string ModName, EntityId ModGroupId)> RetrieveWinningPluginsInLoadout(IDb db, LoadoutId loadoutId)
    {
        return db.Connection.Query<(string PluginFileName, bool IsEnabled, string ModName, EntityId ModGroupId)>($"""
            SELECT * FROM cengine.WinningLoadoutPluginGroups({db}, {loadoutId}, {LocationId.Game.Value})
            """
        );
    }

    public static IObservable<IChangeSet<(string PluginName, int SortIndex, EntityId ItemId, bool? IsEnabled, string? ModName, EntityId? ModGroupId), SortItemKey<string>>>
        ObservePluginSortOrder(IConnection connection, SortOrderId sortOrderId, LoadoutId loadoutId)
    {
        return connection.Query<(string PluginName, int SortIndex, EntityId ItemId, bool? IsEnabled, string? ModName, EntityId? ModGroupId)>($"""
            SELECT * FROM cengine.PluginSortOrderWithLoadoutData({connection}, {sortOrderId.Value}, {loadoutId}, {LocationId.Game.Value})
            """
            )
            .Observe(table => PluginReactiveSortItem.MakeKey(table.PluginName));
    }
}
