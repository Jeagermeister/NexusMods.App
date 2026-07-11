using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.GuidedInstallers;
using Apocrypha.App.UI;

namespace Apocrypha.Games.FOMOD.UI;

public static class Services
{
    public static IServiceCollection AddGuidedInstallerUi(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddTransient<IGuidedInstaller, GuidedInstallerUi>()

            .AddViewModel<GuidedInstallerWindowViewModel, IGuidedInstallerWindowViewModel>()
            .AddViewModel<GuidedInstallerStepViewModel, IGuidedInstallerStepViewModel>()

            .AddView<FooterStepperView, IFooterStepperViewModel>()
            .AddView<GuidedInstallerStepView, IGuidedInstallerStepViewModel>()
            .AddView<GuidedInstallerGroupView, IGuidedInstallerGroupViewModel>()
            .AddView<GuidedInstallerOptionView, IGuidedInstallerOptionViewModel>();
    }
}
