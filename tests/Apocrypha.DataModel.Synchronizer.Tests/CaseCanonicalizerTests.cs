using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Sdk.Games;
using NexusMods.Paths;

namespace Apocrypha.DataModel.Synchronizer.Tests;

/// <summary>
/// Covers the Linux-only case where two mods declare the same directory with different casing.
/// </summary>
public class CaseCanonicalizerTests : IDisposable
{
    private readonly AbsolutePath _root;

    public CaseCanonicalizerTests()
    {
        _root = FileSystem.Shared
            .GetKnownPath(KnownPath.TempDirectory)
            .Combine($"apocrypha-case-{Guid.NewGuid():N}");
        _root.CreateDirectory();
    }

    public void Dispose()
    {
        if (_root.DirectoryExists())
            _root.DeleteDirectory(recursive: true);
        GC.SuppressFinalize(this);
    }

    private CaseCanonicalizer Create()
    {
        var locations = GameLocations.Create(
            ImmutableDictionary<LocationId, AbsolutePath>.Empty.Add(LocationId.Game, _root)
        );

        return new CaseCanonicalizer(OSInformation.Shared, locations, NullLogger.Instance);
    }

    [SkippableFact]
    public void DeploysIntoTheDirectoryCasingThatAlreadyExists()
    {
        Skip.IfNot(OSInformation.Shared.IsUnix(), "Case folding only matters on a case-sensitive filesystem");

        // F4SE reads Data/F4SE/Plugins; a mod that declares "plugins" must land in the same folder
        // rather than creating a second one the game will never open.
        _root.Combine("Data/F4SE/Plugins").CreateDirectory();

        var resolved = Create().ResolveUnder(_root, "Data/F4SE/plugins/BakaFramework.dll");

        resolved.Should().Be(_root.Combine("Data/F4SE/Plugins/BakaFramework.dll"));
    }

    [SkippableFact]
    public void FoldsLaterModsIntoTheCasingChosenByTheFirst()
    {
        Skip.IfNot(OSInformation.Shared.IsUnix(), "Case folding only matters on a case-sensitive filesystem");

        // Neither directory exists yet, so the first mod in the apply fixes the casing and the
        // second is folded into it — otherwise a fresh install reproduces the split.
        var canonicalizer = Create();

        var first = canonicalizer.ResolveUnder(_root, "Data/F4SE/Plugins/First.dll");
        var second = canonicalizer.ResolveUnder(_root, "Data/F4SE/plugins/Second.dll");

        second.Parent.Should().Be(first.Parent);
    }

    [SkippableFact]
    public void PreservesTheCasingOfTheFileNameItself()
    {
        Skip.IfNot(OSInformation.Shared.IsUnix(), "Case folding only matters on a case-sensitive filesystem");

        // Only directories are containers that mods intend to share; a file is opened by name.
        _root.Combine("Data").CreateDirectory();

        var resolved = Create().ResolveUnder(_root, "Data/NanoSuit.esp");

        resolved.FileName.Should().Be("NanoSuit.esp");
    }

    [SkippableFact]
    public void CountsOnlyThePathsItActuallyRewrote()
    {
        Skip.IfNot(OSInformation.Shared.IsUnix(), "Case folding only matters on a case-sensitive filesystem");

        _root.Combine("Data/Textures").CreateDirectory();
        var canonicalizer = Create();

        canonicalizer.ResolveUnder(_root, "Data/Textures/a.dds");
        canonicalizer.RemappedCount.Should().Be(0, "the casing already matched");

        canonicalizer.ResolveUnder(_root, "Data/textures/b.dds");
        canonicalizer.RemappedCount.Should().Be(1, "'textures' was rewritten to 'Textures'");
    }
}
