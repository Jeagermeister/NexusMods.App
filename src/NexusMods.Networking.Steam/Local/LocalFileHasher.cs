using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Paths;
using NexusMods.Sdk.Hashes;

namespace NexusMods.Networking.Steam.Local;

/// <summary>
/// A single installed file that was hashed locally and whose SHA1 matched the Steam depot manifest,
/// i.e. a verified-vanilla file.
/// </summary>
public sealed record VerifiedFile
{
    /// <summary>Path of the file relative to the game install directory.</summary>
    public required RelativePath Path { get; init; }

    /// <summary>The full set of hashes computed from the local file.</summary>
    public required MultiHash Hash { get; init; }
}

/// <summary>
/// The outcome of verifying one depot manifest against the files on disk.
/// </summary>
public sealed record ManifestVerificationResult
{
    /// <summary>The manifest that was verified against.</summary>
    public required Manifest Manifest { get; init; }

    /// <summary>Files present on disk whose SHA1 matched the manifest (safe to treat as vanilla).</summary>
    public required IReadOnlyList<VerifiedFile> VerifiedFiles { get; init; }

    /// <summary>Number of manifest files not found on disk.</summary>
    public required int MissingCount { get; init; }

    /// <summary>Number of manifest files present but whose contents differ from the manifest (modified).</summary>
    public required int MismatchCount { get; init; }

    /// <summary>Total number of real (non-directory) files in the manifest.</summary>
    public required int TotalFiles { get; init; }

    /// <summary>Number of files that matched (== <see cref="VerifiedFiles"/>.Count).</summary>
    public int MatchedCount => VerifiedFiles.Count;

    /// <summary>True when every real file in the manifest was found and matched (a pristine vanilla install).</summary>
    public bool IsFullyVerified => MissingCount == 0 && MismatchCount == 0 && TotalFiles > 0;
}

/// <summary>
/// Hashes the files of an installed game on disk and verifies them against a Steam depot
/// <see cref="Manifest"/>. Only files whose SHA1 matches the manifest are reported as verified,
/// which keeps the resulting hash data correct even if the install has been partially modified
/// (mismatched or missing files are counted and skipped, never recorded as vanilla).
/// </summary>
/// <remarks>
/// Linux fork: part of the login-free, download-free local recognition pipeline. Reads the game's
/// own files (no CDN download) and cross-checks them against Steam's on-disk manifest SHA1s.
/// </remarks>
public class LocalFileHasher
{
    private readonly ILogger<LocalFileHasher> _logger;

    public LocalFileHasher(ILogger<LocalFileHasher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Hash and verify every real file listed in <paramref name="manifest"/> against the files under
    /// <paramref name="gameDirectory"/>.
    /// </summary>
    /// <param name="gameDirectory">The game's install directory (the depot root on disk).</param>
    /// <param name="manifest">The parsed Steam depot manifest to verify against.</param>
    /// <param name="progress">Optional progress reporter, reporting the fraction (0..1) of bytes processed.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task<ManifestVerificationResult> VerifyAndHashAsync(
        AbsolutePath gameDirectory,
        Manifest manifest,
        IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        // Real files only; directory entries in Steam manifests have no chunks.
        var realFiles = manifest.Files.Where(f => f.Chunks.Length > 0).ToArray();
        var totalBytes = realFiles.Aggregate(0UL, (sum, f) => sum + f.Size.Value);
        var processedBytes = 0UL;

        var verified = new List<VerifiedFile>(capacity: realFiles.Length);
        var missing = 0;
        var mismatch = 0;

        foreach (var file in realFiles)
        {
            token.ThrowIfCancellationRequested();

            // Sanitize the manifest path (Windows depots use backslashes) so it resolves on this OS.
            var relativePath = RelativePath.FromUnsanitizedInput(file.Path.ToString());
            var filePath = gameDirectory / relativePath;

            if (!filePath.FileExists)
            {
                missing++;
                _logger.LogDebug("Manifest file not found on disk: {Path}", relativePath);
                progress?.Report(totalBytes == 0 ? 0 : (double)(processedBytes += file.Size.Value) / totalBytes);
                continue;
            }

            MultiHash hash;
            await using (var stream = filePath.Read())
            {
                hash = await MultiHasher.HashStream(stream, token);
            }

            if (!hash.Sha1.Equals(file.Hash))
            {
                mismatch++;
                _logger.LogDebug("SHA1 mismatch (modified file), skipping: {Path}", relativePath);
            }
            else
            {
                verified.Add(new VerifiedFile { Path = relativePath, Hash = hash });
            }

            processedBytes += file.Size.Value;
            progress?.Report(totalBytes == 0 ? 1 : (double)processedBytes / totalBytes);
        }

        _logger.LogInformation(
            "Verified manifest {ManifestId} (depot {DepotId}): {Matched}/{Total} files matched, {Missing} missing, {Mismatch} modified",
            manifest.ManifestId.Value, manifest.DepotId.Value, verified.Count, realFiles.Length, missing, mismatch);

        return new ManifestVerificationResult
        {
            Manifest = manifest,
            VerifiedFiles = verified,
            MissingCount = missing,
            MismatchCount = mismatch,
            TotalFiles = realFiles.Length,
        };
    }
}
