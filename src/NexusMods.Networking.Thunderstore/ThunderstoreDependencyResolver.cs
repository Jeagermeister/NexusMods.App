using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Thunderstore;
using NexusMods.Abstractions.Thunderstore.DTOs;

namespace NexusMods.Networking.Thunderstore;

/// <summary>
/// Resolves the full dependency closure of a Thunderstore package version. Thunderstore
/// dependencies are exact-version strings, so this is plain graph traversal, not constraint
/// solving: BFS from the root, and when the same package is requested at two versions the
/// higher one wins (the Thunderstore/r2modman convention).
/// </summary>
public class ThunderstoreDependencyResolver
{
    /// <summary>
    /// A hard cap on closure size as a tripwire against pathological or malicious dependency data.
    /// </summary>
    private const int MaxResolvedPackages = 512;

    private readonly IThunderstoreApiClient _apiClient;
    private readonly ILogger<ThunderstoreDependencyResolver> _logger;

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
        var queue = new Queue<PackageVersionRef>();
        queue.Enqueue(root);

        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (chosen.TryGetValue(current.Package, out var already) &&
                PackageVersionRef.CompareVersions(already.Version.Version, current.Version) >= 0)
                continue;

            if (chosen.Count >= MaxResolvedPackages)
            {
                errors.Add($"Dependency closure exceeded {MaxResolvedPackages} packages; aborting resolution");
                break;
            }

            var dto = await _apiClient.GetVersion(current, cancellationToken);
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
                    queue.Enqueue(dependency);
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

        return new ResolutionResult(packages, errors);
    }
}
