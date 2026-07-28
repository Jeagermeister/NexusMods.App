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
            // Cancel the OAuth job too -- closing the overlay alone leaves the login flow
            // waiting on a callback that will never be completed by the user.
            if (job.Status.IsActive())
                job.AsContext().Cancel();
            Close();
        });
    }

    public ReactiveCommand Cancel { get; }
    [Reactive] public Uri? Uri { get; private set; }
}
