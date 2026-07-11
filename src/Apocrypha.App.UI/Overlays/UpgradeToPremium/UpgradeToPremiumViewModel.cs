using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.Sdk;
using R3;

namespace Apocrypha.App.UI.Overlays;

public interface IUpgradeToPremiumViewModel : IOverlayViewModel
{

    public ReactiveCommand<Unit> CommandCancel { get; }
    
    public ReactiveCommand<Unit> CommandLearnMore { get; }

    public ReactiveCommand<Unit> CommandUpgrade { get; }
}

public class UpgradeToPremiumViewModel : AOverlayViewModel<IUpgradeToPremiumViewModel>, IUpgradeToPremiumViewModel
{
    public UpgradeToPremiumViewModel(
        IOSInterop osInterop)
    {
        CommandCancel = new ReactiveCommand(_ => base.Close());
        CommandLearnMore = new ReactiveCommand(execute: _ => osInterop.OpenUri(NexusModsUrlBuilder.LearnAboutPremiumUri));
        CommandUpgrade = new ReactiveCommand(execute: _ => osInterop.OpenUri(NexusModsUrlBuilder.UpgradeToPremiumUri));
    }

    public ReactiveCommand<Unit> CommandCancel { get; }
    public ReactiveCommand<Unit> CommandLearnMore { get; }
    public ReactiveCommand<Unit> CommandUpgrade { get; }
}
