using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Sdk.Jobs;
using R3;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.Overlays.Login;

public class NexusLoginOverlayViewModel : AOverlayViewModel<INexusLoginOverlayViewModel>, INexusLoginOverlayViewModel
{
    public NexusLoginOverlayViewModel(IJob job)
    {
        if (job.Definition is IOAuthJob oAuthJob)
        {
            oAuthJob.LoginUriSubject
                .ObserveOnUIThreadDispatcher()
                .Subscribe(this, static (uri, self) => self.Uri = uri);
        }

        Cancel = new ReactiveCommand(execute: _ =>
        {
            // TODO: cancel job
            Close();
        });
    }

    public ReactiveCommand Cancel { get; }
    [Reactive] public Uri? Uri { get; private set; }
}
