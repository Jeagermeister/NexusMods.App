using System.Collections.Concurrent;
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

    /// <summary>Number of manifest files that could not be read (permissions, dangling symlinks, I/O errors).</summary>
    public required int UnreadableCount { get; init; }

    /// <summary>Total number of real (non-directory) files in the manifest.</summary>
    public required int TotalFiles { get; init; }

    /// <summary>Number of files that matched (== <see cref="VerifiedFiles"/>.Count).</summary>
    public int MatchedCount => VerifiedFiles.Count;

    /// <summary>True when every real file in the manifest was found and matched (a pristine vanilla install).</summary>
    public bool IsFullyVerified => MissingCount == 0 && MismatchCount == 0 && UnreadableCount == 0 && TotalFiles > 0;
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

    // Hashing is disk-bound and MultiHasher reads in small chunks, so a large read buffer plus a
    // few files in flight makes a substantial difference on SSDs without drowning spinning disks.
    private const int ReadBufferSize = 1 << 20;
    private static readonly int MaxConcurrentFiles = Math.Min(Environment.ProcessorCount, 8);

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

        var verified = new ConcurrentBag<VerifiedFile>();
        var missing = 0;
        var mismatch = 0;
        var unreadable = 0;
        var processedBytes = 0UL;
        var reportedBytes = 0UL;
        var progressLock = new object();

        void ReportFileProcessed(ulong fileSize)
        {
            var newTotal = Interlocked.Add(ref processedBytes, fileSize);
            if (progress is null)
                return;
            lock (progressLock)
            {
                // Completions from parallel files can report out of order; only ever move forward.
                if (newTotal < reportedBytes)
                    return;
                reportedBytes = newTotal;
                progress.Report(totalBytes == 0 ? 1 : (double)newTotal / totalBytes);
            }
        }

        await Parallel.ForEachAsync(
            realFiles,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentFiles, CancellationToken = token },
            async (file, ct) =>
            {
                // Sanitize the manifest path (Windows depots use backslashes) so it resolves on this OS.
                var relativePath = RelativePath.FromUnsanitizedInput(file.Path.ToString());

                try
                {
                    // Manifests are local trusted data, but a path that escapes the game directory
                    // must never be followed.
                    var pathString = relativePath.Path;
                    if (pathString == ".." || pathString.StartsWith("../", StringComparison.Ordinal) || pathString.Contains("/../", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref unreadable);
                        _logger.LogWarning("Manifest path escapes the game directory, skipping: {Path}", relativePath);
                        return;
                    }

                    var filePath = gameDirectory / relativePath;

                    if (!filePath.FileExists)
                    {
                        Interlocked.Increment(ref missing);
                        _logger.LogDebug("Manifest file not found on disk: {Path}", relativePath);
                        return;
                    }

                    // A file whose size differs from the manifest cannot be vanilla; skip hashing
                    // it entirely. On modded installs this avoids reading most changed content.
                    if (filePath.FileInfo.Size.Value != file.Size.Value)
                    {
                        Interlocked.Increment(ref mismatch);
                        _logger.LogDebug("Size differs from manifest (modified file), skipping: {Path}", relativePath);
                        return;
                    }

                    MultiHash hash;
                    await using (var stream = filePath.Read())
                    await using (var buffered = new BufferedStream(stream, ReadBufferSize))
                    {
                        hash = await MultiHasher.HashStream(buffered, ct);
                    }

                    if (!hash.Sha1.Equals(file.Hash))
                    {
                        Interlocked.Increment(ref mismatch);
                        _logger.LogDebug("SHA1 mismatch (modified file), skipping: {Path}", relativePath);
                    }
                    else
                    {
                        verified.Add(new VerifiedFile { Path = relativePath, Hash = hash });
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // One unreadable file (permission-denied leftover, dangling symlink, transient
                    // I/O error) must not abort the whole recognition run; count it and move on.
                    Interlocked.Increment(ref unreadable);
                    _logger.LogWarning(e, "Could not read file, skipping: {Path}", relativePath);
                }
                finally
                {
                    ReportFileProcessed(file.Size.Value);
                }
            });

        _logger.LogInformation(
            "Verified manifest {ManifestId} (depot {DepotId}): {Matched}/{Total} files matched, {Missing} missing, {Mismatch} modified, {Unreadable} unreadable",
            manifest.ManifestId.Value, manifest.DepotId.Value, verified.Count, realFiles.Length, missing, mismatch, unreadable);

        return new ManifestVerificationResult
        {
            Manifest = manifest,
            VerifiedFiles = verified.OrderBy(static f => f.Path.Path, StringComparer.Ordinal).ToList(),
            MissingCount = missing,
            MismatchCount = mismatch,
            UnreadableCount = unreadable,
            TotalFiles = realFiles.Length,
        };
    }
}
