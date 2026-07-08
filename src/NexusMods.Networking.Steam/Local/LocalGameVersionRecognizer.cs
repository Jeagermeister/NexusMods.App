using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.NexusModsApi;
using OperatingSystem = NexusMods.Abstractions.Games.FileHashes.Values.OperatingSystem;

namespace NexusMods.Networking.Steam.Local;

/// <inheritdoc cref="ILocalGameVersionRecognizer" />
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
        var versionName = $"{installation.Game.DisplayName} (local)";

        // Pass 1: resolve which depots actually need hashing. Depots already recorded in the overlay
        // are skipped without reading a byte; if one is recorded but the game has no local version
        // definition yet (e.g. it was indexed via `steam local-index` without a game id), only the
        // missing definition is created.
        var toHash = new List<Manifest>();
        foreach (var manifestId in manifestIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_fileHashesService.TryGetLocalSteamManifestStatus(manifestId, gameId, out var hasDefinition))
            {
                if (hasDefinition)
                {
                    versionDefinitionWritten = true;
                }
                else if (!versionDefinitionWritten && gameId.HasValue)
                {
                    // The depot id is recovered from the cached file name when available; the service
                    // anchors to the already-recorded manifest by its id, so a missing file is fine.
                    LocalManifestReader.TryFindManifestFile(depotCache, manifestId, out _, out var recordedDepotId);
                    await _fileHashesService.AddLocalSteamVersionAsync(appId, recordedDepotId, manifestId, versionName, gameId, operatingSystem, [], cancellationToken);
                    versionDefinitionWritten = true;
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

        // Pass 2: hash and record. Progress is weighted by bytes, not depot count, so one huge depot
        // doesn't leave the bar sitting at 0% while tiny language depots each count the same.
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

            // Write one version definition for the whole game (on the first recognised depot); the rest
            // record only their file manifest, which is what GetGameFiles needs to stop flagging them modified.
            var writeVersionDefinition = !versionDefinitionWritten && gameId.HasValue;

            await _fileHashesService.AddLocalSteamVersionAsync(
                appId,
                manifest.DepotId,
                manifest.ManifestId,
                writeVersionDefinition ? versionName : $"local-{manifest.ManifestId.Value}",
                writeVersionDefinition ? gameId : null,
                operatingSystem,
                verifiedFiles,
                cancellationToken);

            if (writeVersionDefinition)
                versionDefinitionWritten = true;

            depotsRecognized++;
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
