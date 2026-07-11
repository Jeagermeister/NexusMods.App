using System.Reactive;
using Avalonia.Media;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.TopBar;

public interface ITopBarViewModel : IViewModelInterface
{
    public string ActiveWorkspaceTitle { get; }

    public string ActiveWorkspaceSubtitle { get; }

    public ReactiveCommand<NavigationInformation, Unit> OpenSettingsCommand { get; }

    public ReactiveCommand<NavigationInformation, Unit> ViewChangelogCommand { get; }
    public ReactiveCommand<Unit, Unit> ViewAppLogsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowWelcomeMessageCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenGitHubCommand { get; }

    public R3.ReactiveCommand<R3.Unit, R3.Unit> LoginCommand { get; }
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenNexusModsProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenNexusModsPremiumCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenNexusModsAccountSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }

    public bool IsLoggedIn { get; }
    public UserRole UserRole { get; }
    public IImage? Avatar { get; }
    public string? Username { get; }

    public IAddPanelDropDownViewModel AddPanelDropDownViewModel { get; set; }
    
    public IPanelTabViewModel? SelectedTab { get; set; }
}
