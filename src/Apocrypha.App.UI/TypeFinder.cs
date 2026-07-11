using Apocrypha.Abstractions.Serialization.ExpressionGenerator;
using Apocrypha.App.UI.Pages;
using Apocrypha.App.UI.Pages.Changelog;
using Apocrypha.App.UI.Pages.CollectionDownload;
using Apocrypha.App.UI.Pages.DebugControls;
using Apocrypha.App.UI.Pages.Diagnostics;
using Apocrypha.App.UI.Pages.Diff.ApplyDiff;
using Apocrypha.App.UI.Pages.Downloads;
using Apocrypha.App.UI.Pages.LibraryPage;
using Apocrypha.App.UI.Pages.LoadoutGroupFilesPage;
using Apocrypha.App.UI.Pages.LoadoutPage;
using Apocrypha.App.UI.Pages.MyGames;
using Apocrypha.App.UI.Pages.MyLoadouts;
using Apocrypha.App.UI.Pages.ObservableInfo;
using Apocrypha.App.UI.Pages.Settings;
using Apocrypha.App.UI.Pages.TextEdit;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI;

internal class TypeFinder : ITypeFinder
{
    public IEnumerable<Type> DescendentsOf(Type type)
    {
        return AllTypes.Where(t => t.IsAssignableTo(type));
    }

    private static IEnumerable<Type> AllTypes => new[]
    {
        // factory context
        typeof(MyGamesPageContext),
        typeof(DiagnosticListPageContext),
        typeof(ApplyDiffPageContext),
        typeof(SettingsPageContext),
        typeof(ChangelogPageContext),
        typeof(TextEditorPageContext),
        typeof(MyLoadoutsPageContext),
        typeof(LoadoutGroupFilesPageContext),
        typeof(LibraryPageContext),
        typeof(LoadoutPageContext),
        typeof(CollectionLoadoutPageContext),
        typeof(ProtocolRegistrationTestPageContext),
        typeof(DownloadsPageContext),

        // workspace context
        typeof(EmptyContext),
        typeof(HomeContext),
        typeof(LoadoutContext),
        typeof(DownloadsContext),
        typeof(CollectionDownloadPageContext),
        typeof(ObservableInfoPageContext),
        typeof(DebugControlsPageContext),

        // other
        typeof(WindowData),
    };
}
