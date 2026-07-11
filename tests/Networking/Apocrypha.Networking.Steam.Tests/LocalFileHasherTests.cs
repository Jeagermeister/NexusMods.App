using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Abstractions.Steam.DTOs;
using Apocrypha.Abstractions.Steam.Values;
using Apocrypha.Networking.Steam.Local;
using NexusMods.Paths;
using Apocrypha.Sdk.Hashes;
using Xunit;

namespace Apocrypha.Networking.Steam.Tests;

/// <summary>
/// Tests for the login-free local recognition hasher: files on disk are verified against a Steam
/// depot manifest, and only SHA1-matching files are reported as vanilla. The manifests here are
/// hand-built DTOs, so no Steam data is needed.
/// </summary>
public class LocalFileHasherTests(IFileSystem fileSystem)
{
    private readonly LocalFileHasher _hasher = new(NullLogger<LocalFileHasher>.Instance);

    [Fact]
    public async Task VerifyAndHash_ClassifiesMatchedModifiedAndMissing_AndSanitizesBackslashPaths()
    {
        var gameDir = CreateTempDir();
        try
        {
            var matchedContent = "vanilla content"u8.ToArray();
            var originalContent = "original stuff!"u8.ToArray();
            var modifiedContent = "different stuf!"u8.ToArray(); // same length: exercises the hash check, not the size shortcut

            WriteFile(gameDir / "Data" / "matched.bin", matchedContent);
            WriteFile(gameDir / "Data" / "modified.bin", modifiedContent);
            // missing.bin is deliberately not written.

            // Windows-style depot paths (backslashes) must resolve on this OS.
            var manifest = MakeManifest(
                RealFile(@"Data\matched.bin", matchedContent),
                RealFile(@"Data\modified.bin", originalContent),
                RealFile("missing.bin", "gone"u8.ToArray()));

            var result = await _hasher.VerifyAndHashAsync(gameDir, manifest);

            result.MatchedCount.Should().Be(1);
            result.MismatchCount.Should().Be(1);
            result.MissingCount.Should().Be(1);
            result.UnreadableCount.Should().Be(0);
            result.TotalFiles.Should().Be(3);
            result.IsFullyVerified.Should().BeFalse();
            result.VerifiedFiles.Should().ContainSingle()
                .Which.Path.ToString().Should().Be("Data/matched.bin");
        }
        finally
        {
            gameDir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAndHash_SizeDifference_CountsAsModified()
    {
        var gameDir = CreateTempDir();
        try
        {
            var manifestContent = "the expected content"u8.ToArray();
            WriteFile(gameDir / "sized.bin", "shorter"u8.ToArray());

            var manifest = MakeManifest(RealFile("sized.bin", manifestContent));

            var result = await _hasher.VerifyAndHashAsync(gameDir, manifest);

            result.MismatchCount.Should().Be(1);
            result.MatchedCount.Should().Be(0);
        }
        finally
        {
            gameDir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAndHash_UnreadableFile_IsCountedNotThrown()
    {
        // As root every file is readable and this scenario cannot be produced.
        if (Environment.IsPrivilegedProcess || !OperatingSystem.IsLinux())
            return;

        var gameDir = CreateTempDir();
        try
        {
            var content = "locked away"u8.ToArray();
            var lockedPath = gameDir / "locked.bin";
            WriteFile(lockedPath, content);
            File.SetUnixFileMode(lockedPath.ToString(), UnixFileMode.None);

            var okContent = "fine"u8.ToArray();
            WriteFile(gameDir / "ok.bin", okContent);

            var manifest = MakeManifest(RealFile("locked.bin", content), RealFile("ok.bin", okContent));

            var result = await _hasher.VerifyAndHashAsync(gameDir, manifest);

            result.UnreadableCount.Should().Be(1);
            result.MatchedCount.Should().Be(1);
            result.IsFullyVerified.Should().BeFalse();
        }
        finally
        {
            File.SetUnixFileMode((gameDir / "locked.bin").ToString(), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            gameDir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAndHash_PathEscapingGameDirectory_IsNeverFollowed()
    {
        var gameDir = CreateTempDir();
        try
        {
            var content = "outside"u8.ToArray();
            // The file exists at the traversal target; it must still not be read.
            WriteFile(gameDir.Parent / "escape-target.bin", content);

            var manifest = MakeManifest(RealFile(@"..\escape-target.bin", content));

            var result = await _hasher.VerifyAndHashAsync(gameDir, manifest);

            result.MatchedCount.Should().Be(0);
            result.UnreadableCount.Should().Be(1);
        }
        finally
        {
            (gameDir.Parent / "escape-target.bin").Delete();
            gameDir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAndHash_Progress_IsMonotonicAndEndsAtOne()
    {
        var gameDir = CreateTempDir();
        try
        {
            var files = new List<Manifest.FileData>();
            for (var i = 0; i < 20; i++)
            {
                var content = Enumerable.Repeat((byte)i, (i + 1) * 100).ToArray();
                WriteFile(gameDir / $"file{i}.bin", content);
                files.Add(RealFile($"file{i}.bin", content));
            }

            var progress = new CollectingProgress();
            var result = await _hasher.VerifyAndHashAsync(gameDir, MakeManifest(files.ToArray()), progress);

            result.MatchedCount.Should().Be(20);
            progress.Values.Should().NotBeEmpty();
            progress.Values.Should().BeInAscendingOrder();
            progress.Values[^1].Should().Be(1.0);
        }
        finally
        {
            gameDir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAndHash_EmptyManifest_ReportsNothingVerified()
    {
        var gameDir = CreateTempDir();
        try
        {
            var result = await _hasher.VerifyAndHashAsync(gameDir, MakeManifest());

            result.TotalFiles.Should().Be(0);
            result.IsFullyVerified.Should().BeFalse();
        }
        finally
        {
            gameDir.DeleteDirectory(recursive: true);
        }
    }

    private AbsolutePath CreateTempDir()
    {
        var dir = fileSystem.FromUnsanitizedFullPath(Path.Combine(Path.GetTempPath(), "nm-hasher-test-" + Guid.NewGuid().ToString("N")));
        dir.CreateDirectory();
        return dir;
    }

    private static void WriteFile(AbsolutePath path, byte[] content)
    {
        path.Parent.CreateDirectory();
        using var stream = path.Create();
        stream.Write(content);
    }

    private static Manifest MakeManifest(params Manifest.FileData[] files) => new()
    {
        ManifestId = ManifestId.From(123456789UL),
        DepotId = DepotId.From(42u),
        CreationTime = DateTimeOffset.UnixEpoch,
        TotalCompressedSize = Size.From(0UL),
        TotalUncompressedSize = Size.From(files.Aggregate(0UL, static (sum, f) => sum + f.Size.Value)),
        Files = files,
    };

    /// <summary>A real (non-directory) manifest entry whose SHA1/size describe <paramref name="content"/>.</summary>
    private static Manifest.FileData RealFile(string path, byte[] content) => new()
    {
        Path = path,
        Size = Size.From((ulong)content.Length),
        Hash = Sha1Value.From(SHA1.HashData(content)),
        Chunks = [DummyChunk()],
    };

    private static Manifest.Chunk DummyChunk() => new()
    {
        ChunkId = Sha1Value.From(new byte[20]),
        Checksum = Crc32Value.From(0),
        Offset = 0,
        CompressedSize = Size.From(0UL),
        UncompressedSize = Size.From(0UL),
    };

    private sealed class CollectingProgress : IProgress<double>
    {
        private readonly List<double> _values = [];
        private readonly object _lock = new();

        public void Report(double value)
        {
            lock (_lock)
            {
                _values.Add(value);
            }
        }

        public IReadOnlyList<double> Values
        {
            get
            {
                lock (_lock)
                {
                    return _values.ToList();
                }
            }
        }
    }
}
