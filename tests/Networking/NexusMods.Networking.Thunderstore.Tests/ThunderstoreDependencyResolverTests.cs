using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.Abstractions.Thunderstore;
using NexusMods.Abstractions.Thunderstore.DTOs;
using Xunit;

namespace NexusMods.Networking.Thunderstore.Tests;

/// <summary>
/// Tests for the dependency-closure resolver against a canned in-memory API: exact-version BFS,
/// highest-version-wins on conflicts, cycle termination, and error reporting for missing or
/// malformed dependencies.
/// </summary>
public class ThunderstoreDependencyResolverTests
{
    [Fact]
    public async Task DependencyPins_AreFloors_ResolvedToLatest()
    {
        // Mod authors rarely bump dependency pins; the ecosystem convention (r2modman) is to
        // install the LATEST version of every dependency. The root the user asked for stays exact.
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-Dep-1.0.0")
            .Add("Team", "Root", "9.9.9")
            .Add("Team", "Dep", "1.0.0")
            .Add("Team", "Dep", "2.0.0")
            .SetLatest("Team", "Root", "9.9.9")
            .SetLatest("Team", "Dep", "2.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Select(x => x.Version.FullName).Should().Equal(
            "Team-Root-1.0.0", // root stays exactly what was requested
            "Team-Dep-2.0.0"); // dependency upgraded from the 1.0.0 pin to latest
        api.FetchCount("Team-Dep-1.0.0").Should().Be(0, "the pinned version should not be fetched when a newer latest exists");
    }

    [Fact]
    public async Task Resolves_SimpleChain()
    {
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-MidA-1.0.0")
            .Add("Team", "MidA", "1.0.0", "Team-Leaf-1.0.0")
            .Add("Team", "Leaf", "1.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Select(x => x.Version.FullName).Should().Equal(
            "Team-Root-1.0.0", // root always first
            "Team-Leaf-1.0.0",
            "Team-MidA-1.0.0");
    }

    [Fact]
    public async Task DiamondConflict_HighestVersionWins()
    {
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-A-1.0.0", "Team-B-1.0.0")
            .Add("Team", "A", "1.0.0", "Team-Shared-1.0.0")
            .Add("Team", "B", "1.0.0", "Team-Shared-10.0.0") // higher, and numerically > "9.x"
            .Add("Team", "Shared", "1.0.0")
            .Add("Team", "Shared", "10.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Select(x => x.Version.FullName)
            .Should().ContainSingle(x => x.StartsWith("Team-Shared-"))
            .Which.Should().Be("Team-Shared-10.0.0");
    }

    [Fact]
    public async Task HigherVersionSeenFirst_LowerRequestIsIgnored()
    {
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-Shared-2.0.0", "Team-Mid-1.0.0")
            .Add("Team", "Mid", "1.0.0", "Team-Shared-1.0.0")
            .Add("Team", "Shared", "1.0.0")
            .Add("Team", "Shared", "2.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Select(x => x.Version.FullName)
            .Should().ContainSingle(x => x.StartsWith("Team-Shared-"))
            .Which.Should().Be("Team-Shared-2.0.0");
        api.FetchCount("Team-Shared-1.0.0").Should().Be(0, "the lower version must be skipped without an API call");
    }

    [Fact]
    public async Task Cycle_TerminatesWithBothPackages()
    {
        var api = new FakeApiClient()
            .Add("Team", "A", "1.0.0", "Team-B-1.0.0")
            .Add("Team", "B", "1.0.0", "Team-A-1.0.0");

        var result = await Resolve(api, "Team-A-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Should().HaveCount(2);
    }

    [Fact]
    public async Task MissingDependency_IsReportedAndResolutionContinues()
    {
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-Gone-1.0.0", "Team-Present-1.0.0")
            .Add("Team", "Present", "1.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.Contains("Team-Gone-1.0.0"));
        result.Packages.Select(x => x.Version.FullName).Should().Contain("Team-Present-1.0.0");
    }

    [Fact]
    public async Task UnparsableDependencyString_IsReported()
    {
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "not a dependency string");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.Contains("not a dependency string"));
        result.Packages.Should().ContainSingle();
    }

    [Fact]
    public async Task MissingRoot_YieldsErrorAndEmptyClosure()
    {
        var api = new FakeApiClient();

        var result = await Resolve(api, "Team-Nothing-1.0.0");

        result.IsComplete.Should().BeFalse();
        result.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task NoDependencies_ResolvesOnlyTheRoot()
    {
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-Dep-1.0.0")
            .Add("Team", "Dep", "1.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0", includeDependencies: false);

        result.IsComplete.Should().BeTrue();
        result.Packages.Select(x => x.Version.FullName).Should().Equal("Team-Root-1.0.0");
    }

    private static Task<ThunderstoreDependencyResolver.ResolutionResult> Resolve(
        FakeApiClient api, string root, bool includeDependencies = true)
    {
        PackageVersionRef.TryParse(root, out var rootRef).Should().BeTrue();
        var resolver = new ThunderstoreDependencyResolver(api, NullLogger<ThunderstoreDependencyResolver>.Instance);
        return resolver.ResolveAsync(rootRef, includeDependencies);
    }

    private sealed class FakeApiClient : IThunderstoreApiClient
    {
        private readonly Dictionary<string, PackageVersionDto> _versions = new();
        private readonly Dictionary<string, PackageVersionDto> _latest = new();
        private readonly Dictionary<string, int> _fetches = new();

        public FakeApiClient Add(string ns, string name, string version, params string[] dependencies)
        {
            var dto = new PackageVersionDto
            {
                Namespace = ns,
                Name = name,
                VersionNumber = version,
                Dependencies = dependencies,
            };
            _versions[dto.VersionRef.FullName] = dto;
            return this;
        }

        /// <summary>Marks an added version as the package's latest (returned by GetPackage).</summary>
        public FakeApiClient SetLatest(string ns, string name, string version)
        {
            _latest[$"{ns}-{name}"] = _versions[$"{ns}-{name}-{version}"];
            return this;
        }

        public int FetchCount(string fullName) => _fetches.GetValueOrDefault(fullName);

        public Task<PackageDto?> GetPackage(PackageRef package, CancellationToken cancellationToken = default)
        {
            if (!_latest.TryGetValue(package.FullName, out var latest)) return Task.FromResult<PackageDto?>(null);
            return Task.FromResult<PackageDto?>(new PackageDto
            {
                Namespace = package.Namespace,
                Name = package.Name,
                Latest = latest,
            });
        }

        public Task<PackageVersionDto?> GetVersion(PackageVersionRef version, CancellationToken cancellationToken = default)
        {
            _fetches[version.FullName] = _fetches.GetValueOrDefault(version.FullName) + 1;
            return Task.FromResult(_versions.GetValueOrDefault(version.FullName));
        }
    }
}
