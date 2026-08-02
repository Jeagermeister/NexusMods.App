using FluentAssertions;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using NSubstitute;

namespace Apocrypha.Games.CreationEngine.Tests;

/// <summary>
/// Review finding B-4: on a Linux box with no <c>Documents</c> directory,
/// <c>KnownPath.MyGamesDirectory</c> resolves to relative junk (<c>'My Games'</c> — verified
/// empirically, not an exception) and the first path combine in FO4/SSE <c>GetLocations</c>
/// throws, so both games silently never register. <see cref="KnownPaths.MyGamesOrFallback"/>
/// is the guard.
/// </summary>
public class MyGamesFallbackTests
{
    /// <summary>
    /// The invariant the game registrations actually need, asserted against the real
    /// FileSystem: whatever this box's environment looks like, the result must be rooted and
    /// combinable. On a dev box with <c>~/Documents</c> this exercises the primary branch; on
    /// the CI runner — which has no <c>Documents</c> directory, the very reason the Creation
    /// Engine games fail to locate there (see <c>Startup.cs</c>) — it exercises the fallback.
    /// </summary>
    [Fact]
    public void ResultIsAlwaysRootedAndCombinable()
    {
        var result = KnownPaths.MyGamesOrFallback(FileSystem.Shared);

        PathHelpers.IsRooted(result.ToString()).Should().BeTrue(
            "an unrooted result is exactly the B-4 junk value that made GetLocations throw");
        result.FileName.ToString().Should().Be("My Games");

        var combined = result / "Fallout4";
        combined.ToString().Should().EndWith("My Games/Fallout4");
    }

    /// <summary>
    /// A healthy resolution must be returned untouched — in particular for Proton-prefix games,
    /// whose overlay FileSystem maps <c>MyGamesDirectory</c> into the wine prefix. Falling back
    /// there would silently move Preferences out of the prefix.
    /// </summary>
    [Fact]
    public void AHealthyResolutionIsReturnedUnchanged()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var healthy = FileSystem.Shared.FromUnsanitizedFullPath("/prefix/drive_c/users/steamuser/Documents/My Games");
        fileSystem.GetKnownPath(KnownPath.MyGamesDirectory).Returns(healthy);

        var result = KnownPaths.MyGamesOrFallback(fileSystem);

        result.Should().Be(healthy);
        fileSystem.DidNotReceive().GetKnownPath(KnownPath.HomeDirectory);
    }

    /// <summary>
    /// The xdg-less shape: MyGames resolves to something unrooted (the real-world value is
    /// built from an empty MyDocuments), so the helper must derive the same path a healthy box
    /// would have — <c>$HOME/Documents/My Games</c> — from the home directory instead.
    /// </summary>
    [Fact]
    public void AnUnrootedResolutionFallsBackToHomeDocuments()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        // FromUnsanitizedFullPath("") reproduces the exact empty AbsolutePath the real
        // BaseFileSystem hands back when Environment.GetFolderPath(MyDocuments) returns "".
        fileSystem.GetKnownPath(KnownPath.MyGamesDirectory).Returns(FileSystem.Shared.FromUnsanitizedFullPath(""));
        fileSystem.GetKnownPath(KnownPath.HomeDirectory).Returns(FileSystem.Shared.FromUnsanitizedFullPath("/home/headless"));

        var result = KnownPaths.MyGamesOrFallback(fileSystem);

        result.ToString().Should().Be("/home/headless/Documents/My Games");
    }
}
