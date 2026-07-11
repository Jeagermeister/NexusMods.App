using Apocrypha.App.UI.WorkspaceSystem;
using R3;

namespace Apocrypha.App.UI.Pages;

public interface IProtocolRegistrationTestPageViewModel : IPageViewModelInterface
{
    ReactiveCommand CommandStartTest { get; }
    ReactiveCommand CommandStopTest { get; }
    IReadOnlyBindableReactiveProperty<bool> IsTestRunning { get; }
    IReadOnlyBindableReactiveProperty<bool> HasTestResult { get; }
    IReadOnlyBindableReactiveProperty<bool> FailedTest { get; }
}
