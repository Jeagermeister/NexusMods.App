using DynamicData;
using JetBrains.Annotations;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Extensions;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.UI.Sdk;
using ObservableCollections;
using OneOf;
using R3;

namespace Apocrypha.App.UI.Pages.LoadoutPage;

public static class LoadoutComponents
{
    public sealed class LoadoutItemIds : ReactiveR3Object, IItemModelComponent<LoadoutItemIds>, IComparable<LoadoutItemIds>
    {
        public int CompareTo(LoadoutItemIds? other) => 0;
        
        private readonly OneOf<ObservableHashSet<LoadoutItemId>, LoadoutItemId[]> _ids;
        private readonly IDisposable? _idsObservable;
        
        public IEnumerable<LoadoutItemId> ItemIds => _ids.Match(
            f0: static x => x.AsEnumerable(),
            f1: static x => x.AsEnumerable()
        );
        
        public LoadoutItemIds(LoadoutItemId itemId)
        {
            _ids = new[] { itemId };
        }
        
        public LoadoutItemIds(IObservable<IChangeSet<LoadoutItemId, EntityId>> childrenItemIdsObservable)
        {
            _ids = new ObservableHashSet<LoadoutItemId>();
            _idsObservable = childrenItemIdsObservable.SubscribeWithErrorLogging(changeSet => _ids.AsT0.ApplyChanges(changeSet));
        }
        
        private bool _isDisposed;
        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                if (disposing)
                {
                    Disposable.Dispose(_idsObservable ?? Disposable.Empty);
                }
            }

            base.Dispose(disposing);
        }
    }
    
    public sealed class ParentCollectionDisabled : ReactiveR3Object, IItemModelComponent<ParentCollectionDisabled>, IComparable<ParentCollectionDisabled>
    {
        public ReactiveCommand<NavigationInformation, NavigationInformation> ButtonCommand { get; } = new(info => info);

        public int CompareTo(ParentCollectionDisabled? other) => 0;
    }
    
    public sealed class LockedEnabledState : ReactiveR3Object, IItemModelComponent<LockedEnabledState>, IComparable<LockedEnabledState>
    {
        public ReactiveCommand<NavigationInformation, NavigationInformation> ButtonCommand { get; } = new(info => info);

        public int CompareTo(LockedEnabledState? other) => 0;
    }

    public sealed class MixLockedAndParentDisabled : ReactiveR3Object, IItemModelComponent<MixLockedAndParentDisabled>, IComparable<MixLockedAndParentDisabled>
    {
        public ReactiveCommand<NavigationInformation, NavigationInformation> ButtonCommand { get; } = new(info => info);
        
        public int CompareTo(MixLockedAndParentDisabled? other) => 0;
    }

    /// <summary>
    /// A single entry in the "Move to collection" submenu. The command is created by
    /// <see cref="MoveToCollectionAction"/> and funnels into its <see cref="MoveToCollectionAction.MoveRequests"/>.
    /// </summary>
    public sealed record MoveToCollectionTarget(CollectionGroupId Id, string Name, ReactiveCommand<Unit> Command);

    /// <summary>
    /// Snapshot of what a row can be moved to: the mutable collections that don't already
    /// contain all of the row's movable items, and whether the row has any movable items at all
    /// (items delivered by a collection are not movable).
    /// </summary>
    public readonly record struct MoveToCollectionState(IReadOnlyList<(CollectionGroupId Id, string Name)> Targets, bool HasMovableItems);

    public sealed class MoveToCollectionAction : ReactiveR3Object, IItemModelComponent<MoveToCollectionAction>, IComparable<MoveToCollectionAction>
    {
        public int CompareTo(MoveToCollectionAction? other) => 0;

        private const string EnabledText = "Move to collection";
        private const string ManagedByCollectionText = "Move to collection (managed by a collection)";
        private const string NoTargetsText = "Move to collection (no other collection)";

        public System.Collections.ObjectModel.ObservableCollection<MoveToCollectionTarget> Targets { get; } = new();

        private readonly BindableReactiveProperty<bool> _isEnabled = new(false);
        public IReadOnlyBindableReactiveProperty<bool> IsEnabled => _isEnabled;

        private readonly BindableReactiveProperty<string> _displayText = new(EnabledText);
        public IReadOnlyBindableReactiveProperty<string> DisplayText => _displayText;

        private readonly Subject<CollectionGroupId> _moveRequests = new();
        public Observable<CollectionGroupId> MoveRequests => _moveRequests;

        private readonly IDisposable _activationDisposable;
        private readonly CompositeDisposable _targetCommandDisposables = new();

        public MoveToCollectionAction(IObservable<MoveToCollectionState> stateObservable)
        {
            _activationDisposable = this.WhenActivated(stateObservable, static (self, state, disposables) =>
            {
                state
                    .ToObservable()
                    .ObserveOnUIThreadDispatcher()
                    .Subscribe(self, static (snapshot, self) => self.ApplyState(snapshot))
                    .AddTo(disposables);
            });
        }

        private void ApplyState(MoveToCollectionState state)
        {
            _targetCommandDisposables.Clear();
            Targets.Clear();

            foreach (var (id, name) in state.Targets)
            {
                var command = new ReactiveCommand<Unit>();
                command
                    .Subscribe((self: this, id), static (_, tuple) => tuple.self._moveRequests.OnNext(tuple.id))
                    .AddTo(_targetCommandDisposables);
                command.AddTo(_targetCommandDisposables);

                Targets.Add(new MoveToCollectionTarget(id, name, command));
            }

            _isEnabled.Value = state.HasMovableItems && Targets.Count > 0;
            _displayText.Value = !state.HasMovableItems
                ? ManagedByCollectionText
                : Targets.Count == 0 ? NoTargetsText : EnabledText;
        }

        private bool _isDisposed;
        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                if (disposing)
                {
                    Disposable.Dispose(_activationDisposable, _targetCommandDisposables, _moveRequests, _isEnabled, _displayText);
                }
            }

            base.Dispose(disposing);
        }
    }

    public sealed class EnabledStateToggle : ReactiveR3Object, IItemModelComponent<EnabledStateToggle>, IComparable<EnabledStateToggle>
    {
        public ReactiveCommand<Unit> CommandToggle { get; } = new();

        private readonly ValueComponent<bool?> _valueComponent;
        public IReadOnlyBindableReactiveProperty<bool?> Value => _valueComponent.Value;

        public int CompareTo(EnabledStateToggle? other)
        {
            var (a, b) = (Value.Value, other?.Value.Value);
            return (a, b) switch
            {
                (null, null) => 0,
                (not null, null) => 1,
                (null, not null) => -1,
                (not null, not null) => a.Value.CompareTo(b.Value),
            };
        }

        private readonly IDisposable _activationDisposable;

        public EnabledStateToggle(ValueComponent<bool?> valueComponent)
        {
            _valueComponent = valueComponent;

            _activationDisposable = this.WhenActivated(static (self, disposables) =>
            {
                self._valueComponent.Activate().AddTo(disposables);
            });
        }

        private bool _isDisposed;
        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                if (disposing)
                {
                    Disposable.Dispose(_activationDisposable, _valueComponent);
                }                
            }

            base.Dispose(disposing);
        }
    }
}

public static class LoadoutColumns
{
    [UsedImplicitly]
    public sealed class Collections : ICompositeColumnDefinition<Collections>
    {
        public static int Compare<TKey>(CompositeItemModel<TKey> a, CompositeItemModel<TKey> b) where TKey : notnull
        {
            var aValue = a.GetOptional<StringComponent>(key: ComponentKey);
            var bValue = b.GetOptional<StringComponent>(key: ComponentKey);
            return aValue.Compare(bValue);
        }

        public const string ColumnTemplateResourceKey = nameof(LoadoutColumns) + "_" + nameof(Collections);
        public static readonly ComponentKey ComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(StringComponent));
        public static string GetColumnHeader() => "Collections";
        public static string GetColumnTemplateResourceKey() => ColumnTemplateResourceKey;
    }

    [UsedImplicitly]
    public sealed class EnabledState : ICompositeColumnDefinition<EnabledState>
    {
        public static int Compare<TKey>(CompositeItemModel<TKey> a, CompositeItemModel<TKey> b) where TKey : notnull
        {
            var aValue = a.GetOptional<LoadoutComponents.EnabledStateToggle>(key: EnabledStateToggleComponentKey);
            var bValue = b.GetOptional<LoadoutComponents.EnabledStateToggle>(key: EnabledStateToggleComponentKey);
            return aValue.Compare(bValue);
        }

        public const string ColumnTemplateResourceKey = nameof(LoadoutColumns) + "_" + nameof(EnabledState);
        public static readonly ComponentKey LoadoutItemIdsComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(LoadoutComponents.LoadoutItemIds));
        public static readonly ComponentKey EnabledStateToggleComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(LoadoutComponents.EnabledStateToggle));
        public static readonly ComponentKey ParentCollectionDisabledComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(LoadoutComponents.ParentCollectionDisabled));
        public static readonly ComponentKey LockedEnabledStateComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(LoadoutComponents.LockedEnabledState));
        public static readonly ComponentKey MixLockedAndParentDisabledComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(LoadoutComponents.MixLockedAndParentDisabled));
        public static readonly ComponentKey ViewModPageComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(SharedComponents.ViewModPageAction));
        public static readonly ComponentKey ViewModFilesComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(SharedComponents.ViewModFilesAction));
        public static readonly ComponentKey UninstallItemComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(SharedComponents.UninstallItemAction));
        public static readonly ComponentKey MoveToCollectionComponentKey = ComponentKey.From(ColumnTemplateResourceKey + "_" + nameof(LoadoutComponents.MoveToCollectionAction));
        public static string GetColumnHeader() => "Actions";
        public static string GetColumnTemplateResourceKey() => ColumnTemplateResourceKey;
    }
}
