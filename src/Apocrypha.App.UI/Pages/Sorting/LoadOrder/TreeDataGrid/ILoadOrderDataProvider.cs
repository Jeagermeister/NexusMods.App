using System.ComponentModel;
using DynamicData;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.Controls;
using Apocrypha.Sdk.Loadouts;
using R3;

namespace Apocrypha.App.UI.Pages.Sorting;

public interface ILoadOrderDataProvider
{
    IObservable<IChangeSet<CompositeItemModel<ISortItemKey>, ISortItemKey>> ObserveLoadOrder(
        ISortOrderVariety sortOrderVariety,
        LoadoutId loadoutId,
        Observable<ListSortDirection> sortDirectionObservable);
}
