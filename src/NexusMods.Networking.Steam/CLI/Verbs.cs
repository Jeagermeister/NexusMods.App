using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Cli;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.Sdk.Hashes;
using NexusMods.Abstractions.Steam;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Networking.Steam.Exceptions;
using NexusMods.Networking.Steam.Local;
using NexusMods.Paths;
using NexusMods.Paths.Extensions;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.NexusModsApi;
using NexusMods.Sdk.ProxyConsole;
using OperatingSystem = NexusMods.Abstractions.Games.FileHashes.Values.OperatingSystem;

namespace NexusMods.Networking.Steam.CLI;

public static class Verbs
{
    internal static IServiceCollection AddSteamVerbs(this IServiceCollection collection) =>
        collection
            .AddModule("steam", "Verbs for interacting with Steam")
            .AddModule("steam app", "Verbs for querying app data")
            .AddVerb(() => IndexSteamApp)
            .AddVerb(() => LocalIndexSteamApp)
            .AddVerb(() => RecognizeSteamGame)
            .AddVerb(() => Login);

    [Verb("steam recognize-game", "Recognises an installed Steam game's version locally across all of its installed depots (no login or download) and records it in the local hash overlay, so the app stops treating the install as fully modified")]
    private static async Task<int> RecognizeSteamGame(
        [Injected] IRenderer renderer,
        [Injected] IGameRegistry gameRegistry,
        [Injected] ILocalGameVersionRecognizer recognizer,
        [Option("a", "app", "The Steam app id of the installed game to recognise")] long app,
        [Injected] CancellationToken token)
    {
        if (app is <= 0 or > uint.MaxValue)
        {
            await renderer.TextLine("Invalid app id '{0}': must be a positive 32-bit unsigned integer.", app);
            return 1;
        }
        var appId = (uint)app;

        var installations = gameRegistry.LocateGameInstallations()
            .Where(installation => installation.LocatorResult.Store == GameStore.Steam
                                   && installation.LocatorResult.StoreIdentifier == appId.ToString())
            .ToArray();

        if (installations.Length == 0)
        {
            await renderer.TextLine("No installed Steam game found with app id {0}.", appId);
            return 1;
        }

        var anyRecognized = false;
        foreach (var installation in installations)
        {
            if (!recognizer.CanRecognize(installation))
            {
                await renderer.TextLine("Cannot recognise {0} locally (no depotcache available).", installation.Game.DisplayName);
                continue;
            }

            await renderer.TextLine("Recognising {0} at {1} ...", installation.Game.DisplayName, installation.LocatorResult.Path);
            var result = await recognizer.RecognizeAsync(installation, progress: null, token);

            await renderer.TextLine(
                "  {0} depots recorded, {1} without cached manifests; {2} verified files, {3} missing, {4} modified.",
                result.DepotsRecognized, result.DepotsSkippedNoManifest, result.TotalVerifiedFiles, result.TotalMissingFiles, result.TotalModifiedFiles);

            anyRecognized |= result.DepotsRecognized > 0;
        }

        // Non-zero when nothing was recorded, so scripts can detect a run that did no work.
        return anyRecognized ? 0 : 1;
    }

    [Verb("steam local-index", "Recognises an installed Steam game version locally by verifying its files against the on-disk depot manifest (no login or download), then records it in the local hash overlay so the app stops treating the install as fully modified")]
    private static async Task<int> LocalIndexSteamApp(
        [Injected] IRenderer renderer,
        [Injected] LocalManifestReader manifestReader,
        [Injected] LocalFileHasher fileHasher,
        [Injected] IFileHashesService fileHashesService,
        [Option("g", "game", "The game install directory (the depot root on disk)")] AbsolutePath game,
        [Option("c", "depotcache", "The Steam depotcache directory (usually <SteamRoot>/depotcache)")] AbsolutePath depotCache,
        [Option("d", "depot", "The Steam depot id")] long depot,
        [Option("m", "manifest", "The Steam manifest id")] string manifest,
        [Option("a", "app", "The Steam app id (optional; recorded on the manifest)", true)] long app,
        [Option("i", "gameId", "The Nexus Mods game id (optional; when set, a version definition is also written)", true)] long gameId,
        [Option("n", "versionName", "A human-friendly version name (optional; defaults to 'local-<manifestId>')", true)] string? versionName,
        [Option("s", "os", "Operating system for the version definition: Windows|MacOS|Linux (optional; defaults to the host OS)", true)] string? os,
        [Injected] CancellationToken token)
    {
        if (depot is <= 0 or > uint.MaxValue)
        {
            await renderer.TextLine("Invalid depot id '{0}': must be a positive 32-bit unsigned integer.", depot);
            return 1;
        }
        if (app is < 0 or > uint.MaxValue)
        {
            await renderer.TextLine("Invalid app id '{0}': must be a 32-bit unsigned integer.", app);
            return 1;
        }
        if (gameId is < 0 or > uint.MaxValue)
        {
            await renderer.TextLine("Invalid Nexus Mods game id '{0}': must be a 32-bit unsigned integer.", gameId);
            return 1;
        }

        var depotId = DepotId.From((uint)depot);
        var appId = AppId.From((uint)app);

        if (!ulong.TryParse(manifest, out var parsedManifestId))
        {
            await renderer.TextLine("Invalid manifest id '{0}': must be an unsigned 64-bit integer", manifest);
            return 1;
        }
        var manifestId = ManifestId.From(parsedManifestId);

        await renderer.TextLine("Reading cached manifest {0} (depot {1}) from {2}", manifestId.Value, depotId.Value, depotCache);
        var parsedManifest = manifestReader.TryReadManifest(depotCache, depotId, manifestId);
        if (parsedManifest is null)
        {
            await renderer.TextLine("Could not read a usable manifest for depot {0} / manifest {1} from {2}. The file may be missing, or its filenames are encrypted (never decrypted by the Steam client on this machine).", depotId.Value, manifestId.Value, depotCache);
            return 1;
        }

        await renderer.TextLine("Hashing and verifying installed files under {0} (this reads every game file; large games take a while)...", game);
        var result = await fileHasher.VerifyAndHashAsync(game, parsedManifest, progress: null, token);

        await renderer.TextLine("Verification: {0}/{1} files matched, {2} missing, {3} modified, {4} unreadable (fully verified: {5})",
            result.MatchedCount, result.TotalFiles, result.MissingCount, result.MismatchCount, result.UnreadableCount, result.IsFullyVerified);

        if (result.MatchedCount == 0)
        {
            await renderer.TextLine("No files matched the manifest; nothing to record. Check that --game points at the correct install and --depot/--manifest match it.");
            return 1;
        }

        var name = string.IsNullOrWhiteSpace(versionName) ? $"local-{manifestId.Value}" : versionName!;
        NexusModsGameId? nexusGameId = gameId > 0 ? NexusModsGameId.From((uint)gameId) : null;
        var operatingSystem = ParseOperatingSystem(os);

        var verifiedFiles = result.VerifiedFiles.Select(f => (f.Path, f.Hash)).ToArray();
        await fileHashesService.AddLocalSteamVersionAsync(appId, depotId, manifestId, name, nexusGameId, operatingSystem, verifiedFiles, token);

        await renderer.TextLine("Recorded {0} verified files as version '{1}' in the local hash overlay.", verifiedFiles.Length, name);

        // Confirm the version is now recognised through the same read path the synchronizer uses.
        var recognisedFileCount = fileHashesService
            .GetGameFiles((GameStore.Steam, new[] { LocatorId.From(manifestId.Value.ToString()) }))
            .Count();
        await renderer.TextLine("GetGameFiles now returns {0} known game files for manifest {1}.", recognisedFileCount, manifestId.Value);

        if (nexusGameId is null)
            await renderer.TextLine("Note: --gameId was not provided, so no version definition was written (only the file manifest). The install will still be recognised as vanilla by the synchronizer.");

        return 0;
    }

    private static OperatingSystem ParseOperatingSystem(string? os)
    {
        if (!string.IsNullOrWhiteSpace(os))
        {
            if (os.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return OperatingSystem.Windows;
            if (os.Equals("MacOS", StringComparison.OrdinalIgnoreCase) || os.Equals("OSX", StringComparison.OrdinalIgnoreCase)) return OperatingSystem.MacOS;
            if (os.Equals("Linux", StringComparison.OrdinalIgnoreCase)) return OperatingSystem.Linux;
        }

        return OSInformation.Shared.MatchPlatform(
            onWindows: () => OperatingSystem.Windows,
            onLinux: () => OperatingSystem.Linux);
    }
    
    [Verb("steam login", "Starts the login process for Steam")]
    private static async Task<int> Login(
        [Injected] IRenderer renderer,
        [Injected] ISteamSession steamSession,
        [Injected] CancellationToken token)
    {
        await steamSession.Connect(token);
        return 0;
    }
    
    
    [Verb("steam app index", "Indexes a Steam app and updates the given output folder")]
    private static async Task<int> IndexSteamApp(
        [Injected] IRenderer renderer,
        [Injected] JsonSerializerOptions jsonSerializerOptions,
        [Injected] ISteamSession steamSession,
        [Option("a", "appId", "The steam app id to index")] long appId,
        [Option("o", "output", "The output folder to write the index to")] AbsolutePath output,
        [Injected] CancellationToken token)
    {
        var steamAppId = AppId.From((uint)appId);
        
        var indentedOptions = new JsonSerializerOptions(jsonSerializerOptions)
        {
            WriteIndented = true,
        };

        RenderingAuthenticationHandler.Renderer = renderer;
        await steamSession.Connect(token);

        await using (var _ = await renderer.WithProgress())
        {
            {
                var productInfo = await steamSession.GetProductInfoAsync(steamAppId, token);

                var hashFolder = output / "hashes";
                hashFolder.CreateDirectory();

                var existingHashes = await LoadExistingHashes(hashFolder, indentedOptions, token);

                // Write the product info to a file
                var productFile = output / "stores" / "steam" / "apps" / (productInfo.AppId + ".json");
                {
                    productFile.Parent.CreateDirectory();
                    await using var outputStream = productFile.Create();
                    await JsonSerializer.SerializeAsync(outputStream, productInfo, indentedOptions,
                        token
                    );
                }

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = token,
                };
                // For each depot and each manifest, download the manifest and index the files
                await Parallel.ForEachAsync(productInfo.Depots, options, async (depot, token) =>
                {
                    await Parallel.ForEachAsync(depot.Manifests, options, async (manifestInfo, token) =>
                    {
                        try
                        {
                            var manifest = await steamSession.GetManifestContents(steamAppId, depot.DepotId, manifestInfo.Value.ManifestId,
                                manifestInfo.Key, token
                            );

                            var manifestPath = output / "stores" / "steam" / "manifests" / (manifest.ManifestId + ".json");
                            {
                                manifestPath.Parent.CreateDirectory();
                                while (true)
                                {
                                    try
                                    {
                                        await using var outputStream = manifestPath.Create();
                                        await JsonSerializer.SerializeAsync(outputStream, manifest, indentedOptions,
                                            token
                                        );
                                        break;
                                    }
                                    catch (IOException)
                                    {
                                        await Task.Delay(1000, token);
                                    }
                                }
                            }

                            await IndexManifest(steamSession, renderer, steamAppId,
                                output, manifest, indentedOptions,
                                existingHashes, options
                            );
                        }
                        catch (FailedToGetRequestCode ex)
                        {
                            await renderer.Text($"Skipping because of: {ex.Message}");
                            return;
                        }
                    });
                });
            }
        }

        return 0;
    }

    private static async Task<ConcurrentBag<Sha1Value>> LoadExistingHashes(AbsolutePath folder, JsonSerializerOptions options, CancellationToken token)
    {
        var bag = new ConcurrentBag<Sha1Value>();
        var hashFiles = folder.EnumerateFiles("*.json", true);
        
        await Parallel.ForEachAsync(hashFiles, token, async (file, token) =>
        {
            try
            {
                await using var stream = file.Read();
                var hash = await JsonSerializer.DeserializeAsync<MultiHash>(stream, options, token);
                bag.Add(hash!.Sha1);
            }
            catch (Exception ex)
            {
                // Ignore errors
                Console.WriteLine($"Error loading hash file {file}: {ex.Message}");
            }
        });

        return bag;
    }

    private static async Task IndexManifest(ISteamSession session, IRenderer renderer, AppId appId, AbsolutePath output, Manifest manifest, JsonSerializerOptions indentedOptions, ConcurrentBag<Sha1Value> existingHashes, ParallelOptions options)
    {
        var writeLock = new SemaphoreSlim(1, 1);
        await Parallel.ForEachAsync(manifest.Files, options, async (file, token) =>
            {
                if (file.Size == Size.Zero)
                    return;
                if (existingHashes.Contains(file.Hash))
                    return;

                await using var progressTask = await renderer.StartProgressTask($"Hashing {file.Path}", maxValue: file.Size.Value);
                await using var stream = session.GetFileStream(appId, manifest, file.Path);
                await using var progressWrapper = new StreamProgressWrapper<ProgressTask>(stream, state: progressTask, notifyWritten: static (progressTask, values) =>
                {
                    var (current, _) = values;
                    var task = progressTask.Increment(current.Value);
                });

                var multiHash = await MultiHasher.HashStream(stream, cancellationToken: token);

                var fileName = multiHash.XxHash3 + ".json";
                var path = output / "hashes" / fileName[2..4] / fileName[2..];

                await writeLock.WaitAsync(token);
                if (!multiHash.Sha1.Equals(file.Hash))
                    throw new InvalidOperationException("Hash mismatch on downloaded file, expected: " + file.Hash + " got: " + multiHash.Sha1);
                
                try
                {
                    path.Parent.CreateDirectory();
                    {
                        await using var outputStream = path.Create();
                        await JsonSerializer.SerializeAsync(outputStream, multiHash, indentedOptions,
                            token
                        );
                    }
                    existingHashes.Add(multiHash.Sha1);
                }
                finally
                {
                    writeLock.Release();
                }
            }
        );
    }
}
