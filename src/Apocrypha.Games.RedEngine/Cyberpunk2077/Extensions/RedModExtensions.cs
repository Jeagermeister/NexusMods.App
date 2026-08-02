using DynamicData;

using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.RedEngine.Cyberpunk2077.Models;
using Apocrypha.Games.RedEngine.Cyberpunk2077.SortOrder;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.RedEngine.Cyberpunk2077.Extensions;

public static class RedModExtensions
{
    public static RedModSortOrderItem.ReadOnly[] RetrieveRedModSortableEntries(this IDb db, SortOrderId sortOrderId)
    {
        return db.Datoms(SortOrderItem.ParentSortOrder, sortOrderId)
            .Select(d => RedModSortOrderItem.Load(db, d.E))
            .Where(si => si.IsValid())
            .OrderBy(si => si.AsSortOrderItem().SortIndex)
            .ToArray();
    }
    
    public static IReadOnlyList<RedModSortItemData> RetrieveRedModSortOrderItems(IDb db, SortOrderId sortOrderId)
    {
        return db.Connection.Query<(string FolderName, int SortIndex, EntityId ItemId)>($"""
            SELECT * FROM redmod.RedModSortOrderItems({db}, {sortOrderId.Value})
            """)
            .Select(row => new RedModSortItemData(
                RelativePath.FromUnsanitizedInput(row.FolderName),
                row.SortIndex
            ))
            .ToList();
    }
    
    public static IEnumerable<(string FolderName, bool IsEnabled, string ModName, EntityId ModGroupId)> RetrieveWinningRedModsInLoadout(IDb db, LoadoutId loadoutId)
    {
        return db.Connection.Query<(string FolderName, bool IsEnabled, string ModName, EntityId ModGroupId)>($"""
                                                           SELECT * FROM redmod.WinningLoadoutRedModGroups({db.Connection}, {loadoutId}, {LocationId.Game.Value})
                                                           """
        );
    }

    public static IObservable<IChangeSet<(string FolderName, int SortIndex, EntityId ItemId, bool? IsEnabled, string? ModName, EntityId? ModGroupId), SortItemKey<string>>>
        ObserveRedModSortOrder(IConnection connection, SortOrderId sortOrderId, LoadoutId loadoutId)
    {
        return connection.Query<(string FolderName, int SortIndex, EntityId ItemId, bool? IsEnabled, string? ModName, EntityId? ModGroupId)>($"""
            SELECT * FROM redmod.RedModSortOrderWithLoadoutData({connection}, {sortOrderId.Value}, {loadoutId}, {LocationId.Game.Value})
            """
            )
            // Folded cache key so the changeset key agrees with the reactive item's Key — a
            // re-cased folder is an update to the same row, not a remove/add of two rows.
            .Observe(table => RedModReactiveSortItem.MakeKey(RelativePath.FromUnsanitizedInput(table.FolderName)));
    }
    
}
