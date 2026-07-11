using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games.FileHashes;
using Apocrypha.Abstractions.Steam.DTOs;
using Apocrypha.Abstractions.Steam.Values;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.NexusModsApi;
using OperatingSystem = Apocrypha.Abstractions.Games.FileHashes.Values.OperatingSystem;

namespace Apocrypha.Networking.Steam.Local;

/// <inheritdoc cref="ILocalGameVersionRecognizer" />
public class LocalGameVersionRecognizer : ILocalGameVersionRecognizer
{
    private readonly ILogger<LocalGameVersionRecognizer> _logger;
    private readonly LocalManifestReader _manifestReader;
    private readonly LocalFileHasher _fileHasher;
    private readonly IFileHashesService _fileHashesService;
    private readonly IJobMonitor _jobMonitor;

    private readonly object _runningJobsLock = new();
    private readonly Dictionary<AbsolutePath, IJobTask<RecognizeGameVersionJob, LocalRecognitionResult>> _runningJobs = new();

    public LocalGameVersionRecognizer(
        ILogger<LocalGameVersionRecognizer> logger,
        LocalManifestReader manifestReader,
        LocalFileHasher fileHasher,
        IFileHashesService fileHashesService,
        IJobMonitor jobMonitor)
    {
        _logger = logger;
        _manifestReader = manifestReader;
        _fileHasher = fileHasher;
        _fileHashesService = fileHashesService;
        _jobMonitor = jobMonitor;
    }

    /// <inheritdoc />
    public bool CanRecognize(GameInstallation installation)
        => installation.LocatorResult.Store == GameStore.Steam
           && installation.LocatorResult.SteamDepotCachePath is not null
           // Version definitions are keyed on the Nexus Mods game id, so recognition can
           // never mark a Nexus-less game (e.g. Thunderstore-only) as a known version.
           && installation.Game.NexusModsGameId.HasValue;

    /// <inheritdoc />
    public IJobTask<RecognizeGameVersionJob, LocalRecognitionResult> RecognizeInBackground(GameInstallation installation)
    {
        // Keyed by install path: a second request for the same installation joins the in-flight
        // run instead of hashing the same game twice.
        var key = installation.LocatorResult.Path;
        lock (_runningJobsLock)
        {
            if (_runningJobs.TryGetValue(key, out var existing))
                return existing;

            var task = _jobMonitor.Begin(new RecognizeGameVersionJob(installation), async ctx =>
            {
                try
                {
                    var progress = new InlineProgress(fraction => ctx.SetPercent(fraction, 1.0));
                    return await RecognizeAsync(installation, progress, ctx.CancellationToken);
                }
                finally
                {
                    lock (_runningJobsLock)
                    {
                        _runningJobs.Remove(key);
                    }
                }
            });
            _runningJobs[key] = task;
            return task;
        }
    }

    /// <inheritdoc />
    public async Task<LocalRecognitionResult> RecognizeAsync(GameInstallation installation, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var locator = installation.LocatorResult;
        if (locator.Store != GameStore.Steam)
            throw new NotSupportedException($"Local version recognition currently supports Steam only, not {locator.Store}.");

        if (locator.SteamDepotCachePath is not { } depotCache)
            throw new InvalidOperationException("This Steam installation does not expose a depotcache path; cannot recognise it locally.");

        var gameDir = locator.Path;

        if (!uint.TryParse(locator.StoreIdentifier, out var appIdValue))
            throw new InvalidOperationException($"Steam store identifier '{locator.StoreIdentifier}' is not a valid app id.");
        var appId = AppId.From(appIdValue);

        NexusModsGameId? gameId = installation.Game.NexusModsGameId.HasValue
            ? installation.Game.NexusModsGameId.Value
            : null;

        var operatingSystem = locator.TargetOS.MatchPlatform(
            onWindows: () => OperatingSystem.Windows,
            onLinux: () => OperatingSystem.Linux);

        // The locator exposes installed manifest ids (depot ids are recovered from the cached file names).
        var manifestIds = locator.LocatorIds
            .Select(id => ulong.TryParse(id.Value, out var value) ? ManifestId.From(value) : (ManifestId?)null)
            .Where(static m => m.HasValue)
            .Select(static m => m!.Value)
            .Distinct()
            .ToArray();

        var depotsProcessed = 0;
        var depotsRecognized = 0;
        var depotsSkipped = 0;
        var totalVerified = 0;
        var totalMissing = 0;
        var totalModified = 0;
        var definitionExists = false;
        (DepotId DepotId, ManifestId ManifestId)? firstRecordedManifest = null;
        var versionName = $"{installation.Game.DisplayName} (local)";

        // Pass 1: resolve which depots actually need hashing. Depots already recorded in the overlay
        // are skipped without reading a byte.
        var toHash = new List<Manifest>();
        foreach (var manifestId in manifestIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_fileHashesService.TryGetLocalSteamManifestStatus(manifestId, gameId, out var hasDefinition))
            {
                if (hasDefinition)
                    definitionExists = true;

                if (firstRecordedManifest is null)
                {
                    // The depot id is recovered from the cached file name when available; the service
                    // anchors to the already-recorded manifest by its id, so a missing file is fine.
                    LocalManifestReader.TryFindManifestFile(depotCache, manifestId, out _, out var recordedDepotId);
                    firstRecordedManifest = (recordedDepotId, manifestId);
                }

                depotsRecognized++;
                _logger.LogInformation("Depot manifest {ManifestId} of {Game} is already recorded; skipping re-hash", manifestId.Value, installation.Game.DisplayName);
                continue;
            }

            var manifest = _manifestReader.TryReadManifestByManifestId(depotCache, manifestId);
            if (manifest is null)
            {
                depotsSkipped++;
                _logger.LogInformation("No cached manifest for installed manifest {ManifestId} of {Game}; skipping that depot", manifestId.Value, installation.Game.DisplayName);
                continue;
            }

            toHash.Add(manifest);
        }

        // Pass 2: hash and record the file manifests. Progress is weighted by bytes, not depot count,
        // so one huge depot doesn't leave the bar sitting at 0% while tiny language depots each count
        // the same. The version definition is NOT written here — see below.
        var totalBytes = toHash.Aggregate(0UL, static (sum, m) => sum + RealBytes(m));
        var completedBytes = 0UL;

        foreach (var manifest in toHash)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestBytes = RealBytes(manifest);
            var completedSoFar = completedBytes;
            var depotProgress = progress is null || totalBytes == 0
                ? null
                : new InlineProgress(fraction => progress.Report((completedSoFar + fraction * manifestBytes) / totalBytes));

            var result = await _fileHasher.VerifyAndHashAsync(gameDir, manifest, depotProgress, cancellationToken);
            depotsProcessed++;
            totalVerified += result.MatchedCount;
            totalMissing += result.MissingCount;
            totalModified += result.MismatchCount + result.UnreadableCount;

            completedBytes += manifestBytes;
            progress?.Report(totalBytes == 0 ? 1.0 : (double)completedBytes / totalBytes);

            if (result.MatchedCount == 0)
            {
                _logger.LogInformation("Depot {DepotId} (manifest {ManifestId}) had no verified files; not recording", manifest.DepotId.Value, manifest.ManifestId.Value);
                continue;
            }

            var verifiedFiles = result.VerifiedFiles.Select(f => (f.Path, f.Hash)).ToArray();

            await _fileHashesService.AddLocalSteamVersionAsync(
                appId,
                manifest.DepotId,
                manifest.ManifestId,
                $"local-{manifest.ManifestId.Value}",
                gameId: null,
                operatingSystem,
                verifiedFiles,
                cancellationToken);

            firstRecordedManifest ??= (manifest.DepotId, manifest.ManifestId);
            depotsRecognized++;
        }

        // Write the game's version definition only now that the whole run has completed: a cancelled
        // or failed run must never mark the version "known" while depots are still unrecorded (the
        // version staying unknown is what lets the user re-run, and the pass-1 skip makes the re-run
        // resume from where it stopped).
        if (gameId.HasValue && !definitionExists && depotsRecognized > 0 && firstRecordedManifest is { } anchor)
        {
            await _fileHashesService.AddLocalSteamVersionAsync(
                appId,
                anchor.DepotId,
                anchor.ManifestId,
                versionName,
                gameId,
                operatingSystem,
                verifiedFiles: [],
                cancellationToken);
        }

        progress?.Report(1.0);

        _logger.LogInformation(
            "Local recognition of {Game}: {Recognized}/{Total} depots recorded, {Skipped} without cached manifests; {Verified} verified files, {Missing} missing, {Modified} modified",
            installation.Game.DisplayName, depotsRecognized, manifestIds.Length, depotsSkipped, totalVerified, totalMissing, totalModified);

        return new LocalRecognitionResult
        {
            DepotsProcessed = depotsProcessed,
            DepotsRecognized = depotsRecognized,
            DepotsSkippedNoManifest = depotsSkipped,
            TotalVerifiedFiles = totalVerified,
            TotalMissingFiles = totalMissing,
            TotalModifiedFiles = totalModified,
        };
    }

    /// <summary>Total bytes of the real (non-directory) files in a manifest; matches what the hasher reads.</summary>
    private static ulong RealBytes(Manifest manifest)
        => manifest.Files.Where(static f => f.Chunks.Length > 0).Aggregate(0UL, static (sum, f) => sum + f.Size.Value);

    /// <summary>
    /// Synchronous <see cref="IProgress{T}"/>. Unlike <see cref="Progress{T}"/> it does not post
    /// reports through a SynchronizationContext, so they cannot arrive late and move a bar backwards.
    /// </summary>
    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
