using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusMods.CLI.Types;
using NexusMods.Sdk;

namespace NexusMods.CLI;

/// <summary>
/// Registers an OS-level URI scheme handler for every enabled <see cref="IIpcProtocolHandler"/>,
/// so each protocol a source registers in DI (nxm, ror2mm, ...) gets picked up automatically —
/// no per-scheme hosted service needed.
/// </summary>
internal class UriSchemeRegistration : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IOSInterop _osInterop;
    private readonly IEnumerable<IIpcProtocolHandler> _handlers;

    public UriSchemeRegistration(
        ILogger<UriSchemeRegistration> logger,
        IOSInterop osInterop,
        IEnumerable<IIpcProtocolHandler> handlers)
    {
        _logger = logger;
        _osInterop = osInterop;
        _handlers = handlers;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var handler in _handlers)
        {
            if (!handler.IsEnabled)
            {
                _logger.LogDebug("Skipping URI scheme registration for disabled handler `{Scheme}`", handler.Protocol);
                continue;
            }

            try
            {
                await _osInterop.RegisterUriSchemeHandler(scheme: handler.Protocol, cancellationToken: stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception while registering handler for {Scheme} links", handler.Protocol);
            }
        }
    }
}
