using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Networking.Thunderstore;
using Apocrypha.Sdk.EventBus;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.CLI.Types.IpcHandlers;

/// <summary>
/// A handler for ror2mm:// urls — Thunderstore's "Install with Mod Manager" one-click links.
/// Downloads the requested package version plus its full dependency closure into the Library.
/// </summary>
// ReSharper disable once InconsistentNaming
public class Ror2mmIpcProtocolHandler : IIpcProtocolHandler
{
    /// <inheritdoc/>
    public string Protocol => Ror2mmUrl.Scheme;

    /// <inheritdoc/>
    public bool IsEnabled => _settingsManager.Get<ThunderstoreSettings>().EnableThunderstore;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Ror2mmIpcProtocolHandler> _logger;
    private readonly ISettingsManager _settingsManager;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// constructor
    /// </summary>
    public Ror2mmIpcProtocolHandler(
        IServiceProvider serviceProvider,
        ILogger<Ror2mmIpcProtocolHandler> logger,
        ISettingsManager settingsManager)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settingsManager = settingsManager;
        _eventBus = serviceProvider.GetRequiredService<IEventBus>();
    }

    /// <inheritdoc/>
    public async Task Handle(string url, CancellationToken cancel)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Ignoring ror2mm link: Thunderstore support is disabled (Settings → Experimental)");
            return;
        }

        _logger.LogDebug("Received ror2mm URL: {Url}", url);

        if (!Ror2mmUrl.TryParseInstallUrl(url, out var versionRef))
        {
            _logger.LogWarning("Unsupported or malformed ror2mm URL: {Url}", url);
            return;
        }

        var thunderstoreLibrary = _serviceProvider.GetRequiredService<IThunderstoreLibrary>();
        var resolver = _serviceProvider.GetRequiredService<ThunderstoreDependencyResolver>();
        var library = _serviceProvider.GetRequiredService<ILibraryService>();

        if (thunderstoreLibrary.IsAlreadyDownloaded(versionRef))
        {
            _logger.LogInformation("Package `{Package}` has already been downloaded and will be skipped", versionRef.FullName);
            _eventBus.Send(new CliMessages.ModDownloadFailed(new FailureReason.AlreadyExists(versionRef.FullName)));
            return;
        }

        _eventBus.Send(new CliMessages.ModDownloadStarted());

        try
        {
            var result = await resolver.ResolveAsync(versionRef, cancellationToken: cancel);
            if (!result.IsComplete)
            {
                var errors = string.Join("; ", result.Errors);
                _logger.LogWarning("Aborting ror2mm install of `{Package}`: dependency resolution failed: {Errors}", versionRef.FullName, errors);
                _eventBus.Send(new CliMessages.ModDownloadFailed(new FailureReason.Unknown(new InvalidOperationException($"Dependency resolution failed: {errors}"))));
                return;
            }

            foreach (var resolved in result.Packages)
            {
                if (thunderstoreLibrary.IsAlreadyDownloaded(resolved.Version))
                {
                    _logger.LogInformation("Dependency `{Package}` is already in the Library; skipping", resolved.Version.FullName);
                    continue;
                }

                var downloadJob = await thunderstoreLibrary.CreateDownloadJob(resolved.Version, cancel);
                var libraryFile = await library.AddDownload(downloadJob);

                // The root package is what the user clicked — surface its completion in the UI.
                if (resolved.Version.Equals(result.Packages[0].Version))
                    _eventBus.Send(new CliMessages.ModDownloadSucceeded(libraryFile.AsLibraryItem()));
            }

            _logger.LogInformation("Completed ror2mm install of `{Package}` ({Count} package(s) in closure)", versionRef.FullName, result.Packages.Count);
        }
        catch (TaskCanceledException)
        {
            // User-initiated cancellation should not be treated as an error
            _logger.LogInformation("ror2mm install cancelled by user for `{Package}`", versionRef.FullName);
        }
        catch (Exception e)
        {
            _eventBus.Send(new CliMessages.ModDownloadFailed(new FailureReason.Unknown(e)));
            throw;
        }
    }
}
