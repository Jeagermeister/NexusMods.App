using FluentAssertions;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Networking.Steam.Local;
using NexusMods.Paths;
using Xunit;

namespace NexusMods.Networking.Steam.Tests;

/// <summary>
/// Tests for the login-free local recognition file-name resolution: given only a manifest id (as the
/// Steam locator exposes at runtime), the cached <c>&lt;depotId&gt;_&lt;manifestId&gt;.manifest</c> file is located
/// and its depot id recovered from the file name.
/// </summary>
public class LocalManifestReaderTests(IFileSystem fileSystem)
{
    [Fact]
    public void TryFindManifestFile_RecoversDepotId_FromCachedFileName()
    {
        var dir = CreateTempDir();
        try
        {
            Touch(dir / "413153_6005910083361727734.manifest");
            Touch(dir / "261551_813532240231811649.manifest");
            Touch(dir / "not-a-manifest.txt");

            var found = LocalManifestReader.TryFindManifestFile(
                dir,
                ManifestId.From(6005910083361727734UL),
                out var manifestPath,
                out var depotId);

            found.Should().BeTrue();
            depotId.Should().Be(DepotId.From(413153));
            manifestPath.FileName.ToString().Should().Be("413153_6005910083361727734.manifest");
        }
        finally
        {
            dir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public void TryFindManifestFile_ReturnsFalse_WhenNoMatchingManifest()
    {
        var dir = CreateTempDir();
        try
        {
            // A different manifest id is present; the requested one is not.
            Touch(dir / "261551_813532240231811649.manifest");

            LocalManifestReader.TryFindManifestFile(
                    dir,
                    ManifestId.From(6005910083361727734UL),
                    out _,
                    out _)
                .Should().BeFalse();
        }
        finally
        {
            dir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public void TryFindManifestFile_DoesNotMatch_ManifestIdSuffixWithoutUnderscore()
    {
        var dir = CreateTempDir();
        try
        {
            // "999_9123.manifest" ends in "9123.manifest" but NOT in "_123.manifest" — the underscore
            // in the suffix is what prevents manifest id 123 from matching it.
            Touch(dir / "999_9123.manifest");

            LocalManifestReader.TryFindManifestFile(
                    dir,
                    ManifestId.From(123UL),
                    out _,
                    out _)
                .Should().BeFalse();
        }
        finally
        {
            dir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public void TryFindManifestFile_ReturnsFalse_WhenDepotPrefixIsNotNumeric()
    {
        var dir = CreateTempDir();
        try
        {
            Touch(dir / "abc_123.manifest");
            Touch(dir / "_123.manifest");

            LocalManifestReader.TryFindManifestFile(
                    dir,
                    ManifestId.From(123UL),
                    out _,
                    out _)
                .Should().BeFalse();
        }
        finally
        {
            dir.DeleteDirectory(recursive: true);
        }
    }

    [Fact]
    public void TryFindManifestFile_ReturnsFalse_WhenDirectoryMissing()
    {
        var dir = CreateTempDir();
        dir.DeleteDirectory(recursive: true);

        LocalManifestReader.TryFindManifestFile(
                dir,
                ManifestId.From(1UL),
                out _,
                out _)
            .Should().BeFalse();
    }

    private AbsolutePath CreateTempDir()
    {
        var dir = fileSystem.FromUnsanitizedFullPath(Path.Combine(Path.GetTempPath(), "nm-manifest-test-" + Guid.NewGuid().ToString("N")));
        dir.CreateDirectory();
        return dir;
    }

    private static void Touch(AbsolutePath path)
    {
        using var _ = path.Create();
    }
}
