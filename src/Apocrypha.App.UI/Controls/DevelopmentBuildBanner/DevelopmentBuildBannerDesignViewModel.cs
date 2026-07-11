using System.Reactive;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.DevelopmentBuildBanner;

public class DevelopmentBuildBannerDesignViewModel : AViewModel<IDevelopmentBuildBannerViewModel>, IDevelopmentBuildBannerViewModel
{
    public ReactiveCommand<Unit, Unit> GiveFeedbackCommand { get; } = ReactiveCommand.Create(() => { });

    public DevelopmentBuildBannerDesignViewModel() { }
}
