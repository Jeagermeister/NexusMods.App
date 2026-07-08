using JetBrains.Annotations;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// The outcome of recognising an installed game's version locally across all of its installed depots.
/// </summary>
[PublicAPI]
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
/// disk and verifying them against the store's cached depot manifests, then recording the verified-vanilla
/// files in the local hash overlay via <see cref="IFileHashesService"/>. This is the in-app driver of the
/// pipeline that the <c>steam local-index</c> / <c>steam recognize-game</c> CLI verbs exercise.
/// </summary>
/// <remarks>
/// Linux fork: lets a user recognise an installed game whose version is not in the shipped hash database,
/// so the synchronizer stops treating the whole install as modified. Currently supports Steam installs.
/// </remarks>
[PublicAPI]
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
