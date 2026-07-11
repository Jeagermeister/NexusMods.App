using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Networking.Thunderstore;
using Apocrypha.Sdk.EventBus;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.CLI.Types.IpcHandlers;

/// <summary>
/// A handler for ror2mm:// urls — Thunderstore's "Install with Mod Manager" one-click links.
/// Downloads the requested package version plus its full dependency closure into the Library.
/// Works for single mods and modpacks alike: modpack-sized closures resolve against the
/// community package index and download in parallel.
/// </summary>
// ReSharper disable once InconsistentNaming
public class Ror2mmIpcProtocolHandler : IIpcProtocolHandler
{
    /// <summary>
    /// Concurrent package downloads per closure. Local constant rather than
    /// <c>DownloadSettings.MaxParallelDownloads</c> to keep the CLI project decoupled from
    /// the collections assembly; 8 keeps a 274-package modpack fast without hammering the CDN.
    /// </summary>
    private const int MaxParallelDownloads = 8;

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

            // NOT filtered on the root alone: a previously-aborted modpack install may have
            // landed the root but not its dependencies — re-clicking the link resumes it.
            var toDownload = result.Packages
                .Where(resolved => !thunderstoreLibrary.IsAlreadyDownloaded(resolved.Version))
                .ToArray();

            if (toDownload.Length == 0)
            {
                _logger.LogInformation("Package `{Package}` and all its dependencies are already downloaded", versionRef.FullName);
                _eventBus.Send(new CliMessages.ModDownloadFailed(new FailureReason.AlreadyExists(versionRef.FullName)));
                return;
            }

            if (toDownload.Length > 1)
                _eventBus.Send(new CliMessages.ModpackDownloadStarted(versionRef.Package.Name, toDownload.Length));

            var knownCommunities = result.Community is null ? null : new[] { result.Community };
            var failures = new ConcurrentBag<(string FullName, Exception Exception)>();
            LibraryFile.ReadOnly rootLibraryFile = default;

            await Parallel.ForEachAsync(
                toDownload,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = cancel },
                async (resolved, token) =>
                {
                    try
                    {
                        var downloadJob = await thunderstoreLibrary.CreateDownloadJob(resolved.Dto, knownCommunities, token);
                        var libraryFile = await library.AddDownload(downloadJob);

                        if (resolved.Version.Package.Equals(versionRef.Package))
                            rootLibraryFile = libraryFile;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        failures.Add((resolved.Version.FullName, e));
                    }
                }
            );

            if (!failures.IsEmpty)
            {
                var failedNames = string.Join(", ", failures.Select(failure => failure.FullName));
                _logger.LogWarning("{Failed} of {Total} package downloads failed for `{Package}`: {Names}", failures.Count, toDownload.Length, versionRef.FullName, failedNames);
            }

            var rootFailed = failures.Any(failure => failure.FullName == versionRef.FullName);
            if (rootFailed)
            {
                _eventBus.Send(new CliMessages.ModDownloadFailed(new FailureReason.Unknown(failures.First(failure => failure.FullName == versionRef.FullName).Exception)));
                return;
            }

            // Resumed runs (root already in the Library, only dependencies downloaded) have no
            // root library file to surface — completion is logged below either way.
            if (rootLibraryFile.IsValid())
                _eventBus.Send(new CliMessages.ModDownloadSucceeded(rootLibraryFile.AsLibraryItem()));

            _logger.LogInformation(
                "Completed ror2mm install of `{Package}` ({Downloaded} downloaded, {Skipped} already present, {Failed} failed)",
                versionRef.FullName, toDownload.Length - failures.Count, result.Packages.Count - toDownload.Length, failures.Count);
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
