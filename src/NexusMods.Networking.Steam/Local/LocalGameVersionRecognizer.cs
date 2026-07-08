using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.NexusModsApi;
using OperatingSystem = NexusMods.Abstractions.Games.FileHashes.Values.OperatingSystem;

namespace NexusMods.Networking.Steam.Local;

/// <summary>
/// The outcome of recognising an installed game's version locally across all of its installed depots.
/// </summary>
public sealed record LocalRecognitionResult
{
    /// <summary>Number of installed depots whose cached manifest was found and hashed.</summary>
    public required int DepotsProcessed { get; init; }

    /// <summary>Number of depots that had at least one verified-vanilla file recorded (or were already recorded).</summary>
    public required int DepotsRecognized { get; init; }

    /// <summary>Number of installed depots skipped because no cached manifest was present in depotcache.</summary>
    public required int DepotsSkippedNoManifest { get; init; }

    /// <summary>Total verified-vanilla files recorded across all depots.</summary>
    public required int TotalVerifiedFiles { get; init; }

    /// <summary>Total manifest files not found on disk (missing) across all depots.</summary>
    public required int TotalMissingFiles { get; init; }

    /// <summary>Total files present but modified (SHA1 mismatch) across all depots.</summary>
    public required int TotalModifiedFiles { get; init; }

    /// <summary>True when at least one depot was recognised.</summary>
    public bool AnyRecognized => DepotsRecognized > 0;
}

/// <summary>
/// Recognises an installed game's version locally (login-free, download-free) by hashing the files on
/// disk and verifying them against the Steam client's cached depot manifests, then recording the
/// verified-vanilla files in the local hash overlay via <see cref="IFileHashesService"/>. This is the
/// in-app driver of the pipeline that the <c>steam local-index</c> CLI verb exercises for a single depot.
/// </summary>
public interface ILocalGameVersionRecognizer
{
    /// <summary>
    /// Whether this installation can be recognised locally (currently: Steam installs that expose a
    /// depotcache path).
    /// </summary>
    bool CanRecognize(GameInstallation installation);

    /// <summary>
    /// Recognise the installed version of <paramref name="installation"/> across all of its installed
    /// depots, recording verified files in the local hash overlay. Idempotent: depots already recorded
    /// are skipped. <paramref name="progress"/> reports the overall fraction (0..1) processed.
    /// </summary>
    Task<LocalRecognitionResult> RecognizeAsync(GameInstallation installation, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class LocalGameVersionRecognizer : ILocalGameVersionRecognizer
{
    private readonly ILogger<LocalGameVersionRecognizer> _logger;
    private readonly LocalManifestReader _manifestReader;
    private readonly LocalFileHasher _fileHasher;
    private readonly IFileHashesService _fileHashesService;

    public LocalGameVersionRecognizer(
        ILogger<LocalGameVersionRecognizer> logger,
        LocalManifestReader manifestReader,
        LocalFileHasher fileHasher,
        IFileHashesService fileHashesService)
    {
        _logger = logger;
        _manifestReader = manifestReader;
        _fileHasher = fileHasher;
        _fileHashesService = fileHashesService;
    }

    /// <inheritdoc />
    public bool CanRecognize(GameInstallation installation)
        => installation.LocatorResult.Store == GameStore.Steam
           && installation.LocatorResult.SteamDepotCachePath is not null;

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
            onLinux: () => OperatingSystem.Linux,
            onOSX: () => OperatingSystem.MacOS);

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
        var versionDefinitionWritten = false;

        for (var i = 0; i < manifestIds.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestId = manifestIds[i];

            var manifest = _manifestReader.TryReadManifestByManifestId(depotCache, manifestId);
            if (manifest is null)
            {
                depotsSkipped++;
                _logger.LogInformation("No cached manifest for installed manifest {ManifestId} of {Game}; skipping that depot", manifestId.Value, installation.Game.DisplayName);
                progress?.Report((double)(i + 1) / manifestIds.Length);
                continue;
            }

            // Scale the hasher's 0..1 progress into this depot's slice of the overall range.
            var index = i;
            var depotProgress = progress is null
                ? null
                : new Progress<double>(fraction => progress.Report((index + fraction) / manifestIds.Length));

            var result = await _fileHasher.VerifyAndHashAsync(gameDir, manifest, depotProgress, cancellationToken);
            depotsProcessed++;
            totalVerified += result.MatchedCount;
            totalMissing += result.MissingCount;
            totalModified += result.MismatchCount;

            if (result.MatchedCount == 0)
            {
                _logger.LogInformation("Depot {DepotId} (manifest {ManifestId}) had no verified files; not recording", manifest.DepotId.Value, manifestId.Value);
                progress?.Report((double)(i + 1) / manifestIds.Length);
                continue;
            }

            var verifiedFiles = result.VerifiedFiles.Select(f => (f.Path, f.Hash)).ToArray();

            // Write one version definition for the whole game (on the first recognised depot); the rest
            // record only their file manifest, which is what GetGameFiles needs to stop flagging them modified.
            var writeVersionDefinition = !versionDefinitionWritten;
            var versionName = writeVersionDefinition
                ? $"{installation.Game.DisplayName} (local)"
                : $"local-{manifest.ManifestId.Value}";

            await _fileHashesService.AddLocalSteamVersionAsync(
                appId,
                manifest.DepotId,
                manifest.ManifestId,
                versionName,
                writeVersionDefinition ? gameId : null,
                operatingSystem,
                verifiedFiles,
                cancellationToken);

            if (writeVersionDefinition)
                versionDefinitionWritten = true;

            depotsRecognized++;
            progress?.Report((double)(i + 1) / manifestIds.Length);
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
}
