using Microsoft.Extensions.Hosting;

namespace NexusMods.Backend.Tracking;

internal partial class EventTracker : BackgroundService
{
    // Apocrypha: the Mixpanel phone-home was removed with the rebrand (KIRO-HANDOFF §23.4).
    // Upstream this partial POSTed the event ring buffer to api-eu.mixpanel.com every 10
    // seconds; the endpoint, the request loop, and the project tokens are gone. The tracker
    // is also no longer registered as a hosted service (ServiceExtensions.AddTracking), so
    // this never executes — it exists only to keep the type shape intact.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
