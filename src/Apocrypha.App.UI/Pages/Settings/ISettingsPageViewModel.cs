using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls.Settings.Section;
using Apocrypha.App.UI.Controls.Settings.SettingEntries;
using Apocrypha.App.UI.WorkspaceSystem;
using R3;

namespace Apocrypha.App.UI.Pages.Settings;

public interface ISettingsPageViewModel : IPageViewModelInterface
{
    ReactiveCommand<Unit> SaveCommand { get; }
    ReactiveCommand<Unit> CancelCommand { get; }

    ReadOnlyObservableCollection<ISettingEntryViewModel> SettingEntries { get; }

    ReadOnlyObservableCollection<ISettingSectionViewModel> Sections { get; }
}
