using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Abstractions.Thunderstore.DTOs;
using Xunit;

namespace Apocrypha.Networking.Thunderstore.Tests;

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

    [Fact]
    public async Task ModpackSizedClosure_ResolvesViaCommunityIndex()
    {
        // A modpack with dozens of dependencies must NOT be resolved with per-package API
        // calls (a real 273-dependency pack would take minutes and trip rate limiting) — the
        // resolver switches to the community's bulk package index.
        var api = new FakeApiClient().SetCommunity("riskofrain2");

        var packDependencies = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            packDependencies.Add($"Team-Mod{i:00}-1.0.0");
            api.Add("Team", $"Mod{i:00}", "1.0.0")
                .Add("Team", $"Mod{i:00}", "2.0.0")
                .AddToIndex("riskofrain2", "Team", $"Mod{i:00}");
        }

        api.Add("Team", "Pack", "1.0.0", packDependencies.ToArray())
            .SetLatest("Team", "Pack", "1.0.0");

        var resolver = new ThunderstoreDependencyResolver(api, NullLogger<ThunderstoreDependencyResolver>.Instance);
        PackageVersionRef.TryParse("Team-Pack-1.0.0", out var rootRef).Should().BeTrue();

        var result = await resolver.ResolveAsync(rootRef);

        result.IsComplete.Should().BeTrue();
        result.Community.Should().Be("riskofrain2");
        result.Packages.Should().HaveCount(21);
        result.Packages.Skip(1).Should().OnlyContain(x => x.Version.Version == "2.0.0", "dependency pins are floors — the index's latest version wins");
        api.IndexFetchCount.Should().Be(1);
        api.PackageFetchCount.Should().Be(1, "only the root's community lookup may use the per-package endpoint");
        api.FetchCount("Team-Pack-1.0.0").Should().Be(1, "the root version is fetched exactly once");

        // The index is cached: an immediate second resolution must not refetch it.
        await resolver.ResolveAsync(rootRef);
        api.IndexFetchCount.Should().Be(1);
    }

    [Fact]
    public async Task IndexMiss_FallsBackToPerPackageApi()
    {
        // Cross-community dependencies may be absent from the index that was fetched; they
        // must still resolve through the per-package endpoint.
        var api = new FakeApiClient().SetCommunity("riskofrain2");

        var packDependencies = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            packDependencies.Add($"Team-Mod{i:00}-1.0.0");
            api.Add("Team", $"Mod{i:00}", "1.0.0");
            if (i != 20) api.AddToIndex("riskofrain2", "Team", $"Mod{i:00}");
        }

        api.SetLatest("Team", "Mod20", "1.0.0"); // resolvable via API only
        api.Add("Team", "Pack", "1.0.0", packDependencies.ToArray())
            .SetLatest("Team", "Pack", "1.0.0");

        var result = await Resolve(api, "Team-Pack-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Should().HaveCount(21);
        result.Packages.Select(x => x.Version.FullName).Should().Contain("Team-Mod20-1.0.0");
    }

    [Fact]
    public async Task EqualVersionPin_ReusesLatestDtoWithoutRefetching()
    {
        // A dependency pinned at what is already the latest version must reuse the DTO from
        // the latest lookup instead of re-fetching the same version.
        var api = new FakeApiClient()
            .Add("Team", "Root", "1.0.0", "Team-Dep-2.0.0")
            .Add("Team", "Dep", "2.0.0")
            .SetLatest("Team", "Root", "1.0.0")
            .SetLatest("Team", "Dep", "2.0.0");

        var result = await Resolve(api, "Team-Root-1.0.0");

        result.IsComplete.Should().BeTrue();
        result.Packages.Select(x => x.Version.FullName).Should().Equal("Team-Root-1.0.0", "Team-Dep-2.0.0");
        api.FetchCount("Team-Dep-2.0.0").Should().Be(0, "the latest lookup already produced this version's DTO");
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
        private readonly Dictionary<string, List<string>> _indexes = new();
        private string? _community;

        public int PackageFetchCount { get; private set; }
        public int IndexFetchCount { get; private set; }

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

        /// <summary>Sets the community every package reports being listed in.</summary>
        public FakeApiClient SetCommunity(string community)
        {
            _community = community;
            return this;
        }

        /// <summary>Adds all added versions of a package to a community's bulk index.</summary>
        public FakeApiClient AddToIndex(string community, string ns, string name)
        {
            if (!_indexes.TryGetValue(community, out var packages)) _indexes[community] = packages = [];
            packages.Add($"{ns}-{name}");
            return this;
        }

        public int FetchCount(string fullName) => _fetches.GetValueOrDefault(fullName);

        public Task<PackageDto?> GetPackage(PackageRef package, CancellationToken cancellationToken = default)
        {
            PackageFetchCount++;
            if (!_latest.TryGetValue(package.FullName, out var latest)) return Task.FromResult<PackageDto?>(null);
            return Task.FromResult<PackageDto?>(new PackageDto
            {
                Namespace = package.Namespace,
                Name = package.Name,
                Latest = latest,
                CommunityListings = _community is null ? [] : [new CommunityListingDto { Community = _community }],
            });
        }

        public Task<PackageVersionDto?> GetVersion(PackageVersionRef version, CancellationToken cancellationToken = default)
        {
            _fetches[version.FullName] = _fetches.GetValueOrDefault(version.FullName) + 1;
            return Task.FromResult(_versions.GetValueOrDefault(version.FullName));
        }

        public async IAsyncEnumerable<PackageIndexEntryDto> GetCommunityPackageIndex(
            string communitySlug,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IndexFetchCount++;
            await Task.Yield();

            foreach (var packageFullName in _indexes.GetValueOrDefault(communitySlug, []))
            {
                var versions = _versions.Values
                    .Where(dto => dto.VersionRef.Package.FullName == packageFullName)
                    .ToArray();
                if (versions.Length == 0) continue;

                yield return new PackageIndexEntryDto
                {
                    Name = versions[0].Name,
                    Owner = versions[0].Namespace,
                    Versions = versions
                        .Select(dto => new PackageIndexVersionDto
                        {
                            Name = dto.Name,
                            VersionNumber = dto.VersionNumber,
                            Dependencies = dto.Dependencies,
                        })
                        .ToArray(),
                };
            }
        }
    }
}
