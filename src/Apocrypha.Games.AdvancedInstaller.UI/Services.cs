using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Games.AdvancedInstaller.UI.EmptyPreview;
using Apocrypha.Games.AdvancedInstaller.UI.ModContent;
using Apocrypha.Games.AdvancedInstaller.UI.Preview;
using Apocrypha.Games.AdvancedInstaller.UI.SelectLocation;
using ModContentView = Apocrypha.Games.AdvancedInstaller.UI.ModContent.ModContentView;

namespace Apocrypha.Games.AdvancedInstaller.UI;

public static class Services
{
    public static IServiceCollection AddAdvancedInstallerUi(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddTransient<IAdvancedInstallerHandler, AdvancedManualInstallerUI>()
            .AddViewModel<AdvancedInstallerPageViewModel, IAdvancedInstallerPageViewModel>()
            .AddViewModel<AdvancedInstallerWindowViewModel, IAdvancedInstallerWindowViewModel>()
            .AddView<FooterView, IFooterViewModel>()
            .AddView<BodyView, IBodyViewModel>()
            .AddView<ModContentView, IModContentViewModel>()
            .AddView<PreviewView, IPreviewViewModel>()
            .AddView<EmptyPreviewView, IEmptyPreviewViewModel>()
            .AddView<SelectLocationView, ISelectLocationViewModel>()
            .AddView<SuggestedEntryView, ISuggestedEntryViewModel>()
            .AddView<AdvancedInstallerPageView, IAdvancedInstallerPageViewModel>()
            .AddView<ModContentTreeEntryView, IModContentTreeEntryViewModel>()
            .AddView<PreviewTreeEntryView, IPreviewTreeEntryViewModel>()
            .AddView<SelectableTreeEntryView, ISelectableTreeEntryViewModel>()
            .AddView<UnsupportedModPageView, IUnsupportedModPageViewModel>()
            .AddView<AdvancedInstallerWindowView, IAdvancedInstallerWindowViewModel>();
    }
}
