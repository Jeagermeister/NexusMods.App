using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Cli;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Sdk.ProxyConsole;

namespace Apocrypha.Networking.Thunderstore.CLI;

public static class Verbs
{
    internal static IServiceCollection AddThunderstoreVerbs(this IServiceCollection collection) =>
        collection
            .AddModule("thunderstore", "Verbs for interacting with Thunderstore")
            .AddVerb(() => ResolvePackage)
            .AddVerb(() => DownloadPackage);

    [Verb("thunderstore resolve", "Resolves a Thunderstore package's dependency closure without downloading anything")]
    private static async Task<int> ResolvePackage(
        [Injected] IRenderer renderer,
        [Injected] IThunderstoreLibrary thunderstoreLibrary,
        [Injected] ThunderstoreDependencyResolver resolver,
        [Option("p", "package", "The package in Namespace-Name form (e.g. bbepis-BepInExPack)")] string package,
        [Option("v", "version", "Exact version (e.g. 5.4.2100); defaults to the latest published version", true)] string? version,
        [Option("n", "noDeps", "Resolve only the package itself, skipping its dependencies", true)] bool noDeps,
        [Injected] CancellationToken token)
    {
        var root = await ResolveRootRef(renderer, thunderstoreLibrary, package, version, token);
        if (root is null) return 1;

        var result = await resolver.ResolveAsync(root.Value, includeDependencies: !noDeps, cancellationToken: token);

        foreach (var resolved in result.Packages)
        {
            await renderer.TextLine("{0}", resolved.Version.FullName);
        }

        foreach (var error in result.Errors)
        {
            await renderer.TextLine("ERROR: {0}", error);
        }

        await renderer.TextLine("Resolved {0} package(s), {1} error(s).", result.Packages.Count, result.Errors.Count);
        return result.IsComplete ? 0 : 1;
    }

    [Verb("thunderstore download", "Resolves a Thunderstore package (plus dependencies) and downloads everything into the Library")]
    private static async Task<int> DownloadPackage(
        [Injected] IRenderer renderer,
        [Injected] IThunderstoreLibrary thunderstoreLibrary,
        [Injected] ThunderstoreDependencyResolver resolver,
        [Injected] ILibraryService libraryService,
        [Option("p", "package", "The package in Namespace-Name form (e.g. bbepis-BepInExPack)")] string package,
        [Option("v", "version", "Exact version (e.g. 5.4.2100); defaults to the latest published version", true)] string? version,
        [Option("n", "noDeps", "Download only the package itself, skipping its dependencies", true)] bool noDeps,
        [Injected] CancellationToken token)
    {
        var root = await ResolveRootRef(renderer, thunderstoreLibrary, package, version, token);
        if (root is null) return 1;

        var result = await resolver.ResolveAsync(root.Value, includeDependencies: !noDeps, cancellationToken: token);
        if (!result.IsComplete)
        {
            foreach (var error in result.Errors)
            {
                await renderer.TextLine("ERROR: {0}", error);
            }
            await renderer.TextLine("Aborting: the dependency closure did not resolve cleanly.");
            return 1;
        }

        var downloaded = 0;
        var skipped = 0;
        foreach (var resolved in result.Packages)
        {
            if (thunderstoreLibrary.IsAlreadyDownloaded(resolved.Version))
            {
                await renderer.TextLine("{0} is already in the Library; skipping.", resolved.Version.FullName);
                skipped++;
                continue;
            }

            await renderer.TextLine("Downloading {0} ...", resolved.Version.FullName);
            var downloadJob = await thunderstoreLibrary.CreateDownloadJob(resolved.Version, token);
            var libraryFile = await libraryService.AddDownload(downloadJob);
            await renderer.TextLine("Added `{0}` to the Library ({1}).", libraryFile.AsLibraryItem().Name, libraryFile.Size);
            downloaded++;
        }

        await renderer.TextLine("Done: {0} downloaded, {1} already present.", downloaded, skipped);
        return 0;
    }

    private static async Task<PackageVersionRef?> ResolveRootRef(
        IRenderer renderer,
        IThunderstoreLibrary thunderstoreLibrary,
        string package,
        string? version,
        CancellationToken token)
    {
        if (!PackageRef.TryParse(package, out var packageRef))
        {
            await renderer.TextLine("Invalid package '{0}': expected Namespace-Name form (e.g. bbepis-BepInExPack).", package);
            return null;
        }

        if (version is null)
        {
            try
            {
                return await thunderstoreLibrary.GetLatestVersion(packageRef, token);
            }
            catch (KeyNotFoundException)
            {
                await renderer.TextLine("Package '{0}' was not found on Thunderstore.", packageRef.FullName);
                return null;
            }
        }

        if (!PackageVersionRef.IsValidVersion(version))
        {
            await renderer.TextLine("Invalid version '{0}': expected major.minor.patch (e.g. 5.4.2100).", version);
            return null;
        }

        return new PackageVersionRef(packageRef, version);
    }
}
