using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Abstractions.Thunderstore.DTOs;

namespace Apocrypha.Networking.Thunderstore;

/// <summary>
/// Resolves the full dependency closure of a Thunderstore package version. Thunderstore
/// dependencies are exact-version strings, so this is plain graph traversal, not constraint
/// solving: BFS from the root, and when the same package is requested at two versions the
/// higher one wins (the Thunderstore/r2modman convention).
///
/// Small closures resolve via per-package experimental-API calls. Once a closure looks
/// modpack-sized the resolver switches to the community's bulk v1 package index — one
/// multi-megabyte request instead of hundreds of sequential API calls (a 273-dependency
/// modpack would otherwise take minutes and trip rate limiting).
/// </summary>
public class ThunderstoreDependencyResolver
{
    /// <summary>
    /// A hard cap on closure size as a tripwire against pathological or malicious dependency data.
    /// </summary>
    private const int MaxResolvedPackages = 512;

    /// <summary>
    /// Once resolved + queued packages exceed this, the community package index is fetched and
    /// the rest of the closure resolves locally. Ordinary mods (a handful of dependencies) stay
    /// on the cheap per-package path; modpacks cross it immediately.
    /// </summary>
    private const int IndexFetchThreshold = 15;

    private static readonly TimeSpan IndexCacheTtl = TimeSpan.FromMinutes(10);

    private readonly IThunderstoreApiClient _apiClient;
    private readonly ILogger<ThunderstoreDependencyResolver> _logger;

    private readonly SemaphoreSlim _indexLock = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<string, (DateTimeOffset FetchedAt, Dictionary<PackageRef, PackageVersionDto> LatestByPackage)> _indexCache = new();

    /// <summary>
    /// Constructor.
    /// </summary>
    public ThunderstoreDependencyResolver(IThunderstoreApiClient apiClient, ILogger<ThunderstoreDependencyResolver> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// One resolved node of the closure.
    /// </summary>
    public sealed record ResolvedPackage(PackageVersionRef Version, PackageVersionDto Dto);

    /// <summary>
    /// The result of a resolution: the closure (root first, dependencies after in stable order)
    /// plus any errors (missing packages, unparsable dependency strings).
    /// </summary>
    public sealed record ResolutionResult(IReadOnlyList<ResolvedPackage> Packages, IReadOnlyList<string> Errors)
    {
        /// <summary>
        /// True if the whole closure resolved without errors.
        /// </summary>
        public bool IsComplete => Errors.Count == 0;

        /// <summary>
        /// The community whose package index was used for resolution, if any. Doubles as the
        /// best-known game scope for the resolved packages.
        /// </summary>
        public string? Community { get; init; }
    }

    /// <summary>
    /// Resolves the dependency closure of the given package version.
    /// </summary>
    public async Task<ResolutionResult> ResolveAsync(
        PackageVersionRef root,
        bool includeDependencies = true,
        CancellationToken cancellationToken = default)
    {
        var chosen = new Dictionary<PackageRef, ResolvedPackage>();
        var errors = new List<string>();
        var latestCache = new Dictionary<PackageRef, PackageVersionDto?>();
        var queue = new Queue<(PackageVersionRef Ref, bool IsRoot)>();
        queue.Enqueue((root, true));

        Dictionary<PackageRef, PackageVersionDto>? index = null;
        string? community = null;
        var indexAttempted = false;

        while (queue.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = item.Ref;
            PackageVersionDto? dto = null;

            if (!item.IsRoot)
            {
                // Modpack-sized closure? Switch to the bulk community index before grinding
                // through hundreds of per-package API calls.
                if (index is null && !indexAttempted && chosen.Count + queue.Count + 1 > IndexFetchThreshold)
                {
                    indexAttempted = true;
                    (community, index) = await TryLoadIndexForAsync(root, cancellationToken);
                }

                // Ecosystem parity (r2modman): dependency pins are FLOORS, not exact versions.
                // Mod authors rarely bump their pins, so honoring them literally installs
                // builds that predate game updates (e.g. RoR2 mods pinning pre-SotS R2API
                // that references assemblies the game no longer ships). Resolve every
                // dependency to its latest published version; only the root the user asked
                // for stays exact.
                if (!latestCache.TryGetValue(current.Package, out var latest))
                {
                    if (index is not null && index.TryGetValue(current.Package, out var fromIndex))
                        latest = fromIndex;
                    else
                        latest = (await _apiClient.GetPackage(current.Package, cancellationToken))?.Latest;
                    latestCache[current.Package] = latest;
                }

                // >= 0: an equal-version latest is the same version — reuse its DTO instead of
                // re-fetching it through the per-version endpoint below.
                if (latest is not null && PackageVersionRef.CompareVersions(latest.VersionNumber, current.Version) >= 0)
                {
                    current = latest.VersionRef;
                    dto = latest;
                }
            }

            if (chosen.TryGetValue(current.Package, out var already) &&
                PackageVersionRef.CompareVersions(already.Version.Version, current.Version) >= 0)
                continue;

            if (chosen.Count >= MaxResolvedPackages)
            {
                errors.Add($"Dependency closure exceeded {MaxResolvedPackages} packages; aborting resolution");
                break;
            }

            dto ??= await _apiClient.GetVersion(current, cancellationToken);
            if (dto is null)
            {
                errors.Add($"Package version `{current}` was not found on Thunderstore");
                continue;
            }

            chosen[current.Package] = new ResolvedPackage(current, dto);

            if (!includeDependencies) break;

            foreach (var dependencyString in dto.Dependencies)
            {
                if (PackageVersionRef.TryParse(dependencyString, out var dependency))
                {
                    queue.Enqueue((dependency, false));
                }
                else
                {
                    errors.Add($"Package `{current}` declares an unparsable dependency `{dependencyString}`");
                }
            }
        }

        // Stable output order: root first, dependencies sorted by full name.
        var packages = new List<ResolvedPackage>(chosen.Count);
        if (chosen.Remove(root.Package, out var rootResolved)) packages.Add(rootResolved);
        packages.AddRange(chosen.Values.OrderBy(x => x.Version.FullName, StringComparer.Ordinal));

        if (errors.Count > 0)
            _logger.LogWarning("Dependency resolution of `{Root}` finished with {Count} error(s): {Errors}", root, errors.Count, string.Join("; ", errors));

        return new ResolutionResult(packages, errors) { Community = community };
    }

    /// <summary>
    /// Determines the root package's community and returns that community's package index
    /// (latest version per package). Best-effort: on any failure resolution falls back to
    /// per-package API calls.
    /// </summary>
    private async Task<(string? Community, Dictionary<PackageRef, PackageVersionDto>? Index)> TryLoadIndexForAsync(
        PackageVersionRef root,
        CancellationToken cancellationToken)
    {
        try
        {
            var package = await _apiClient.GetPackage(root.Package, cancellationToken);
            var community = package?.CommunityListings.FirstOrDefault()?.Community;
            if (community is null)
            {
                _logger.LogWarning("No community listing found for `{Root}`; resolving per-package instead of via an index", root.Package.FullName);
                return (null, null);
            }

            return (community, await GetOrFetchIndexAsync(community, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to load the community package index for `{Root}`; resolving per-package instead", root.Package.FullName);
            return (null, null);
        }
    }

    private async Task<Dictionary<PackageRef, PackageVersionDto>> GetOrFetchIndexAsync(string community, CancellationToken cancellationToken)
    {
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexCache.TryGetValue(community, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < IndexCacheTtl)
                return cached.LatestByPackage;

            var stopwatch = Stopwatch.StartNew();
            var map = new Dictionary<PackageRef, PackageVersionDto>();

            await foreach (var entry in _apiClient.GetCommunityPackageIndex(community, cancellationToken))
            {
                // Versions are newest-first per API contract, but pick the maximum defensively.
                PackageIndexVersionDto? latest = null;
                foreach (var version in entry.Versions)
                {
                    if (latest is null || PackageVersionRef.CompareVersions(version.VersionNumber, latest.VersionNumber) > 0)
                        latest = version;
                }

                if (latest is not null)
                    map[entry.PackageRef] = latest.ToPackageVersionDto(entry.Owner);
            }

            _indexCache[community] = (DateTimeOffset.UtcNow, map);
            _logger.LogInformation("Fetched the `{Community}` package index: {Count} packages in {Elapsed:0.0}s", community, map.Count, stopwatch.Elapsed.TotalSeconds);
            return map;
        }
        finally
        {
            _indexLock.Release();
        }
    }
}
