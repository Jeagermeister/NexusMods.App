using System.Reactive;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.DevelopmentBuildBanner;

public interface IDevelopmentBuildBannerViewModel : IViewModelInterface
{
    public ReactiveCommand<Unit, Unit> GiveFeedbackCommand { get; }
}
