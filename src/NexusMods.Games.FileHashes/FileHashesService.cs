using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.EpicGameStore.Values;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.Abstractions.Games.FileHashes.Models;
using NexusMods.Abstractions.GOG.Values;
using NexusMods.Sdk.Settings;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Games.FileHashes.DTOs;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Storage;
using NexusMods.MnemonicDB.Storage.RocksDbBackend;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Hashes;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.NexusModsApi;
using BuildId = NexusMods.Abstractions.GOG.Values.BuildId;
using Connection = NexusMods.MnemonicDB.Connection;
using OperatingSystem = NexusMods.Abstractions.Games.FileHashes.Values.OperatingSystem;

namespace NexusMods.Games.FileHashes;

internal sealed class FileHashesService : IFileHashesService, IDisposable, IHostedService
{
    private const string DefaultLanguage = "en-US";
    
    private readonly ScopedAsyncLock _lock = new();
    private readonly FileHashesServiceSettings _settings;
    private readonly IFileSystem _fileSystem;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly Dictionary<AbsolutePath, ConnectedDb> _databases;
    private readonly IServiceProvider _provider;
    private readonly AbsolutePath _hashDatabaseLocation;

    /// <summary>
    /// The currently connected database (if any)
    /// </summary>
    private ConnectedDb? _currentDb;

    /// <summary>
    /// Linux fork: a writable overlay database holding locally-recognised game versions. It is kept
    /// separate from the read-only shipped/embedded database and unioned into every read path. New
    /// versions are written here by <see cref="AddLocalSteamVersionAsync"/> (login-free local
    /// recognition). Reads always observe the latest committed state via <see cref="Connection.Db"/>.
    /// </summary>
    private OverlayDb? _overlayDb;

    private readonly ILogger<FileHashesService> _logger;
    private readonly IQueryEngine _queryEngine;

    private record ConnectedDb(IDb Db, DatomStore Store, Backend Backend, DatabaseInfo DatabaseInfo);

    private sealed record OverlayDb(Connection Connection, DatomStore Store, Backend Backend);

    public FileHashesService(
        ILogger<FileHashesService> logger,
        ISettingsManager settingsManager,
        IFileSystem fileSystem,
        HttpClient httpClient,
        JsonSerializerOptions jsonSerializerOptions,
        IServiceProvider provider)
    {
        _logger = logger;
        _httpClient = httpClient;
        _jsonSerializerOptions = jsonSerializerOptions;
        _fileSystem = fileSystem;
        _settings = settingsManager.Get<FileHashesServiceSettings>();
        _databases = new Dictionary<AbsolutePath, ConnectedDb>();
        _provider = provider;
        _queryEngine = provider.GetRequiredService<IQueryEngine>();

        _hashDatabaseLocation = _settings.HashDatabaseLocation.ToPath(_fileSystem);
        _hashDatabaseLocation.CreateDirectory();
    }

    private ConnectedDb OpenDb(DatabaseInfo databaseInfo)
    {
        try
        {
            if (_databases.TryGetValue(databaseInfo.Path, out var existing))
                return existing;

            _logger.LogInformation("Opening hash database at {Path} for {Timestamp}", databaseInfo.Path, databaseInfo.CreationTime);
            var backend = new Backend(readOnly: true);
            var settings = new DatomStoreSettings
            {
                Path = databaseInfo.Path,
            };

            var store = new DatomStore(_provider.GetRequiredService<ILogger<DatomStore>>(), settings, backend);
            var connection = new Connection(_provider.GetRequiredService<ILogger<Connection>>(), store, _provider, [], readOnlyMode: true, prefix: "hashes", queryEngine: _queryEngine);
            var connectedDb = new ConnectedDb(connection.Db, store, backend, databaseInfo);

            _databases[databaseInfo.Path] = connectedDb;
            return connectedDb;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening database at {Path}", databaseInfo.Path);
            throw;
        }
    }

    /// <summary>
    /// Linux fork: opens (or reuses) the writable local overlay database. Unlike the shipped database
    /// this is opened read-write so locally-recognised versions can be appended to it, and it persists
    /// across runs at <c>{HashDatabaseLocation}/local-overlay</c>. It uses the same prefix and query
    /// engine as the read-only databases so the FileHashes model attributes align.
    /// </summary>
    private OverlayDb EnsureOverlayOpen()
    {
        if (_overlayDb is not null)
            return _overlayDb;

        var overlayPath = _hashDatabaseLocation / "local-overlay";
        overlayPath.CreateDirectory();

        _logger.LogInformation("Opening writable local hash overlay at {Path}", overlayPath);
        var backend = new Backend();
        var settings = new DatomStoreSettings
        {
            Path = overlayPath,
        };
        var store = new DatomStore(_provider.GetRequiredService<ILogger<DatomStore>>(), settings, backend);
        var connection = new Connection(_provider.GetRequiredService<ILogger<Connection>>(), store, _provider, [], prefix: "hashes", queryEngine: _queryEngine);

        _overlayDb = new OverlayDb(connection, store, backend);
        return _overlayDb;
    }

    /// <summary>
    /// Linux fork: the set of databases that read paths query, in priority order. The shipped/embedded
    /// database is queried first, then the writable local overlay (if opened). Each returned <see cref="IDb"/>
    /// is a self-contained snapshot; entity ids are only valid within their own database, so callers must
    /// keep cross-entity matching within a single db.
    /// </summary>
    private IEnumerable<IDb> ReadDbs()
    {
        if (_currentDb is not null)
            yield return _currentDb.Db;
        if (_overlayDb is not null)
            yield return _overlayDb.Connection.Db;
    }

    private record struct DatabaseInfo(AbsolutePath Path, DateTimeOffset CreationTime);

    private IEnumerable<DatabaseInfo> ExistingDBs()
    {
        return _hashDatabaseLocation
            .EnumerateDirectories(recursive: false)
            .Where(d => !d.FileName.EndsWith("_tmp"))
            .Select(path =>
            {
                // Format is "{guid}_{timestamp}"
                var parts = path.FileName.Split('_');
                if (parts.Length != 2 || !ulong.TryParse(parts[1], out var timestamp)) return Optional<DatabaseInfo>.None;
                return new DatabaseInfo(Path: path, CreationTime: DateTimeOffset.FromUnixTimeSeconds((long)timestamp));
            })
            .Where(static optional => optional.HasValue)
            .Select(static optional => optional.Value)
            .OrderByDescending(static databaseInfo => databaseInfo.CreationTime);
    }

    /// <inheritdoc />
    public async Task CheckForUpdate(bool forceUpdate = false)
    {
        await CheckForUpdateCore(forceUpdate);
    }

    /// <inheritdoc />
    public IEnumerable<VanityVersion> GetKnownVanityVersions(NexusModsGameId nexusModsGameId)
    {
        return GetVersionDefinitions(nexusModsGameId)
            .Select(v => VanityVersion.From(v.Name))
            .ToList();
    }

    private List<VersionDefinition.ReadOnly> GetVersionDefinitions(NexusModsGameId nexusModsGameId)
    {
        // Linux fork: union the shipped database with the writable local overlay.
        return ReadDbs()
            .SelectMany(db => VersionDefinition.All(db))
            .Where(v => v.GameId == nexusModsGameId)
            .ToList();
    }

    private bool ShouldCheckForUpdate()
    {
        if (!GameHashesReleaseFileName.FileExists) return true;
        var lastUpdated = GameHashesReleaseFileName.FileInfo.LastWriteTimeUtc;
        var diff = DateTime.UtcNow - lastUpdated;
        return diff >= _settings.HashDatabaseUpdateInterval;
    }


    private async Task CheckForUpdateCore(bool forceUpdate, CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync();

        // Linux fork: when remote updates are disabled we never contact Nexus infrastructure
        // (github.com/Nexus-Mods/game-hashes) at runtime. We use the newest local database, or fall
        // back to the embedded snapshot shipped with the build. See FileHashesServiceSettings.EnableRemoteUpdates.
        if (!_settings.EnableRemoteUpdates)
        {
            if (!forceUpdate && ExistingDBs().TryGetFirst(out var localLatest))
            {
                _currentDb = OpenDb(localLatest);
                return;
            }

            var embedded = await AddEmbeddedDatabase(cancellationToken);
            if (embedded.HasValue)
            {
                _currentDb = OpenDb(embedded.Value);
                return;
            }

            // As a last resort, try any local database we may already have.
            if (ExistingDBs().TryGetFirst(out var fallback))
            {
                _currentDb = OpenDb(fallback);
                return;
            }

            _logger.LogError("Remote hash-database updates are disabled and no local or embedded database is available; game hashes functionality will be unavailable");
            return;
        }

        var existingDatabases = ExistingDBs().ToArray();
        var shouldCheckForUpdate = forceUpdate || existingDatabases.Length == 0 || ShouldCheckForUpdate();

        if (!shouldCheckForUpdate && existingDatabases.TryGetFirst(out var latestDatabase))
        {
            _currentDb = OpenDb(latestDatabase);
            return;
        }

        Manifest? latestReleaseManifest = null;
        if (shouldCheckForUpdate)
        {
            latestReleaseManifest = await FetchLatestReleaseManifest(GameHashesReleaseFileName, cancellationToken: cancellationToken);
        }

        if (existingDatabases.Length == 0)
        {
            var embeddedDatabaseInfo = await AddEmbeddedDatabase(cancellationToken: cancellationToken);
            if (latestReleaseManifest is null)
            {
                if (!embeddedDatabaseInfo.HasValue)
                {
                    _logger.LogError("Failed to add embedded game hashes database and failed to fetch latest release manifest, game hashes functionality will be unavailable");
                    return;
                }

                _logger.LogWarning("Failed to fetch latest release manifest, defaulting to embedded game hashes database which may be out-of-date");
                _currentDb = OpenDb(embeddedDatabaseInfo.Value);
                return;
            }

            Debug.Assert(latestReleaseManifest is not null, "should've returned if we didn't have a manifest");
            if (embeddedDatabaseInfo.HasValue)
            {
                existingDatabases = ExistingDBs().ToArray();
                Debug.Assert(existingDatabases.Length >= 1, $"should have at least one database but found {existingDatabases.Length}");
            }
        }

        if (latestReleaseManifest is null && existingDatabases.Length == 0)
        {
            _logger.LogError("Failed to fetch the latest release manifest and failed to use the embedded database, game hashes functionality will be unavailable");
            return;
        }

        if (latestReleaseManifest is null || existingDatabases[0].CreationTime.ToUnixTimeSeconds() >= latestReleaseManifest.CreatedAt.ToUnixTimeSeconds())
        {
            _currentDb = OpenDb(existingDatabases[0]);
            return;
        }

        _logger.LogInformation("Fetching latest games hashes database");
        var releaseDatabaseInfo = await AddReleaseDatabase(latestReleaseManifest, cancellationToken);
        if (!releaseDatabaseInfo.HasValue) return;

        _currentDb = OpenDb(releaseDatabaseInfo.Value);
    }

    private async ValueTask<Manifest?> FetchLatestReleaseManifest(AbsolutePath storagePath, CancellationToken cancellationToken)
    {
        const int defaultTimeout = 15;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(delay: TimeSpan.FromSeconds(defaultTimeout));

        try
        {
            await using var fileStream = storagePath.Create();
            await using (var httpStream = await _httpClient.GetStreamAsync(_settings.GithubManifestUrl, cancellationToken: cts.Token))
            {
                await httpStream.CopyToAsync(fileStream, cancellationToken: cts.Token);
            }

            fileStream.Position = 0;
            return await JsonSerializer.DeserializeAsync<Manifest>(fileStream, _jsonSerializerOptions, cancellationToken: cts.Token);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to fetch latest release manifest from `{Url}`", _settings.GithubManifestUrl);
            return null;
        }
    }

    private async ValueTask<Optional<DatabaseInfo>> AddEmbeddedDatabase(CancellationToken cancellationToken)
    {
        try
        {
            var streamFactory = new EmbeddedResourceStreamFactory<FileHashesService>(resourceName: "games_hashes_db.zip");
            await using var archiveStream = await streamFactory.GetStreamAsync();
            var creationTime = ApplicationConstants.IsDebug ? DateTimeOffset.UnixEpoch : ApplicationConstants.BuildDate;

            var path = await AddDatabase(archiveStream, creationTime, cancellationToken);
            return new DatabaseInfo(path, creationTime);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to add embedded database");
            return Optional<DatabaseInfo>.None;
        }
    }

    private async ValueTask<Optional<DatabaseInfo>> AddReleaseDatabase(Manifest releaseManifest, CancellationToken cancellationToken)
    {
        try
        {
            await using var httpStream = await _httpClient.GetStreamAsync(_settings.GameHashesDbUrl, cancellationToken: cancellationToken);
            var path = await AddDatabase(httpStream, releaseManifest.CreatedAt, cancellationToken: cancellationToken);
            return new DatabaseInfo(path, releaseManifest.CreatedAt);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to add release database from {Url}", _settings.GameHashesDbUrl);
            return Optional<DatabaseInfo>.None;
        }
    }

    private async ValueTask<AbsolutePath> AddDatabase(
        Stream archiveStream,
        DateTimeOffset databaseCreationTime,
        CancellationToken cancellationToken)
    {
        var name = $"{Guid.NewGuid()}_{databaseCreationTime.ToUnixTimeSeconds()}";

        await using var archivePath = new TemporaryPath(_fileSystem, _hashDatabaseLocation / $"{name}.zip");
        await using (var fileStream = archivePath.Path.Create())
        {
            await archiveStream.CopyToAsync(fileStream, cancellationToken: cancellationToken);
        }

        await using var extractionDirectory = new TemporaryPath(_fileSystem, _hashDatabaseLocation / $"{name}_tmp");
        await using (var fileStream = archivePath.Path.Read())
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
        {
            foreach (var fileEntry in zipArchive.Entries)
            {
                var destinationPath = extractionDirectory.Path.Combine(fileEntry.FullName);
                destinationPath.Parent.CreateDirectory();

                await using var entryStream = fileEntry.Open();
                await using var outputStream = destinationPath.Create();
                await entryStream.CopyToAsync(outputStream, cancellationToken: cancellationToken);
            }
        }

        var finalDirectory = _hashDatabaseLocation / name;
        Directory.Move(
            sourceDirName: extractionDirectory.Path.ToNativeSeparators(OSInformation.Shared),
            destDirName: finalDirectory.ToNativeSeparators(OSInformation.Shared)
        );

        return finalDirectory;
    }

    private AbsolutePath GameHashesReleaseFileName => _hashDatabaseLocation / _settings.ReleaseFilePath;

    /// <inheritdoc />
    public async ValueTask<IDb> GetFileHashesDb()
    {
        if (_currentDb is not null)
            return _currentDb.Db;

        // Call core since we're already inside a lock
        await CheckForUpdateCore(false);

        return Current;
    }

    /// <inheritdoc/>
    public IEnumerable<GameFileRecord> GetGameFiles(LocatorIdsWithGameStore locatorIdsWithGameStore)
    {
        var (gameStore, locatorIds) = locatorIdsWithGameStore;

        // Linux fork: results are unioned across the shipped database and the writable local overlay.
        // For per-id stores (Steam/EGS) each locator id is resolved in the first database that contains
        // it (shipped preferred), which avoids yielding the same file twice when both databases know a
        // version. GOG resolution is stateful (collects builds/products across ids), so it is run once
        // per database; the overlay currently only ever holds Steam data, so it contributes nothing there.
        if (gameStore == GameStore.GOG)
        {
            foreach (var db in ReadDbs())
            {
                foreach (var record in GetGogGameFiles(db, locatorIds))
                    yield return record;
            }
        }
        else if (gameStore == GameStore.Steam)
        {
            foreach (var id in locatorIds)
            {
                if (!ulong.TryParse(id.Value, out var parsedId))
                    continue;

                var manifestId = ManifestId.From(parsedId);

                foreach (var db in ReadDbs())
                {
                    if (!SteamManifest.FindByManifestId(db, manifestId).TryGetFirst(out var firstManifest))
                        continue;

                    foreach (var file in firstManifest.Files)
                    {
                        yield return new GameFileRecord
                        {
                            Path = (LocationId.Game, file.Path),
                            Size = file.Hash.Size,
                            MinimalHash = file.Hash.MinimalHash,
                            Hash = file.Hash.XxHash3,
                        };
                    }

                    // Resolved in this database; don't duplicate from lower-priority databases.
                    break;
                }
            }
        }
        else if (gameStore == GameStore.EGS)
        {
            foreach (var locatorId in locatorIds)
            {
                var egManifestId = ManifestHash.FromUnsanitized(locatorId.Value);

                var found = false;
                foreach (var db in ReadDbs())
                {
                    if (!EpicGameStoreBuild.FindByManifestHash(db, egManifestId).TryGetFirst(out var firstManifest))
                        continue;

                    found = true;
                    foreach (var file in firstManifest.Files)
                    {
                        yield return new GameFileRecord
                        {
                            Path = (LocationId.Game, file.Path),
                            Size = file.Hash.Size,
                            MinimalHash = file.Hash.MinimalHash,
                            Hash = file.Hash.XxHash3,
                        };
                    }

                    break;
                }

                if (!found)
                    _logger.LogWarning("No EGS manifest found for {ManifestId}", egManifestId.Value);
            }
        }
        else
        {
            throw new NotSupportedException("No way to get game files for: " + gameStore);
        }
    }

    /// <summary>
    /// GOG file resolution for a single database. Extracted so it can be run against both the shipped
    /// database and the local overlay (see <see cref="GetGameFiles"/>).
    /// </summary>
    private IEnumerable<GameFileRecord> GetGogGameFiles(IDb db, IEnumerable<LocatorId> locatorIds)
    {
        HashSet<GogBuild.ReadOnly> gogBuilds = [];
        HashSet<ProductId> gogProducts = [];
        Dictionary<EntityId, GogManifest.ReadOnly> gogManifests = [];

        // So first we find all the valid build Ids, and then assume that everything else is a product Id
        foreach (var id in locatorIds)
        {
            if (!ulong.TryParse(id.Value, out var parsedId))
                continue;

            var gogId = BuildId.From(parsedId);

            if (GogBuild.FindByBuildId(db, gogId).TryGetFirst(out var firstBuild))
            {
                gogBuilds.Add(firstBuild);
                continue;
            }

            var productId = ProductId.From(parsedId);
            gogProducts.Add(productId);
        }

        // Now we emit all the files from the build products, and then also from any secondary products
        foreach (var build in gogBuilds)
        {
            foreach (var depot in build.Depots)
            {
                // We only care about the productId of the build, and the productIds of the secondary products
                if (!(depot.ProductId == build.ProductId || gogProducts.Contains(depot.ProductId)))
                    continue;

                // If there is a language setting for the files, they have to be the same as the default language
                if (!(depot.Languages.Count == 0 || depot.Languages.Contains(DefaultLanguage)))
                    continue;

                gogManifests[depot.Manifest.Id] = depot.Manifest;
            }
        }

        foreach (var (_, manifest) in gogManifests)
        {
            foreach (var file in manifest.Files)
            {
                yield return new GameFileRecord
                {
                    Path = (LocationId.Game, file.Path),
                    Size = file.Hash.Size,
                    MinimalHash = file.Hash.MinimalHash,
                    Hash = file.Hash.XxHash3,
                };
            }
        }
    }

    /// <inheritdoc />
    public IDb Current => _currentDb?.Db ?? throw new InvalidOperationException("No database connected");

    /// <inheritdoc />
    public bool TryGetVanityVersion(LocatorIdsWithGameStore locatorIdsWithGameStore, out VanityVersion version)
    {
        if (TryGetGameVersionDefinition(locatorIdsWithGameStore, out var versionDefinition))
        {
            version = VanityVersion.From(versionDefinition.Name);
            return true;
        }

        version = VanityVersion.DefaultValue;
        return false;
    }

    private bool TryGetGameVersionDefinition(
        LocatorIdsWithGameStore locatorIdsWithGameStore,
        out VersionDefinition.ReadOnly versionDefinition)
    {
        // Linux fork: evaluate the shipped database and the writable local overlay, keeping the best
        // match. Version<->manifest/build matching relies on entity ids that are only valid within a
        // single database, so each database is evaluated independently and the highest-scoring match
        // wins (the shipped database is preferred on ties because it is enumerated first).
        versionDefinition = default(VersionDefinition.ReadOnly);
        var found = false;
        var bestScore = int.MinValue;

        foreach (var db in ReadDbs())
        {
            if (TryGetGameVersionDefinitionInDb(db, locatorIdsWithGameStore, out var candidate, out var score) && score > bestScore)
            {
                versionDefinition = candidate;
                bestScore = score;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetGameVersionDefinitionInDb(
        IDb db,
        LocatorIdsWithGameStore locatorIdsWithGameStore,
        out VersionDefinition.ReadOnly versionDefinition,
        out int score)
    {
        var (gameStore, locatorIds) = locatorIdsWithGameStore;

        versionDefinition = default(VersionDefinition.ReadOnly);
        score = 0;
        if (gameStore == GameStore.GOG)
        {
            List<GogBuild.ReadOnly> gogBuilds = [];

            foreach (var gogId in locatorIds)
            {
                if (!ulong.TryParse(gogId.Value, out var parsedId))
                {
                    _logger.LogWarning("Unable to parse `{Raw}` as ulong", gogId);
                    return false;
                }

                var hasBuild = GogBuild.FindByBuildId(db, BuildId.From(parsedId)).TryGetFirst(out var gogBuild);
                if (hasBuild) gogBuilds.Add(gogBuild);
            }

            if (gogBuilds.Count == 0)
            {
                _logger.LogDebug("No GOG builds found");
                return false;
            }

            var hasVersionDefinition = VersionDefinition.All(db)
                .Select(version =>
                {
                    var matchingIdCount = gogBuilds.Count(g => version.GogBuildsIds.Contains(g));
                    return (matchingIdCount, version);
                })
                .Where(row => row.matchingIdCount > 0)
                .OrderByDescending(row => row.matchingIdCount)
                .TryGetFirst(out var best);

            if (!hasVersionDefinition)
            {
                _logger.LogDebug("No matching version definition found");
                return false;
            }

            versionDefinition = best.version;
            score = best.matchingIdCount;
        }
        else if (gameStore == GameStore.Steam)
        {
            List<SteamManifest.ReadOnly> steamManifests = [];
            
            foreach (var steamId in locatorIds)
            {
                if (!ulong.TryParse(steamId.Value, out var parsedId))
                {
                    _logger.LogDebug("Steam locator {Raw} metadata is not a valid ulong", steamId);
                    return false;
                }

                var hasManifest = SteamManifest.FindByManifestId(db, ManifestId.From(parsedId)).TryGetFirst(out var steamManifest);
                if (hasManifest) steamManifests.Add(steamManifest);
            }

            if (steamManifests.Count == 0)
            {
                _logger.LogDebug("No Steam manifests found for locator metadata");
                return false;
            }
            
            var wasFound = VersionDefinition.All(db)
                .Select(version =>
                {
                    var matchingIdCount = steamManifests.Count(g => version.SteamManifestsIds.Contains(g));
                    return (matchingIdCount, version);
                })
                .Where(row => row.matchingIdCount > 0)
                .OrderByDescending(row => row.matchingIdCount)
                .TryGetFirst(out var best);

            if (!wasFound)
            {
                _logger.LogDebug("No version found for locator metadata");
                return false;
            }

            versionDefinition = best.version;
            score = best.matchingIdCount;
        }
        else if (gameStore == GameStore.EGS)
        {
            var versionsByManifestHash = VersionDefinition.All(db)
                .SelectMany(version =>
                    {
                        if (VersionDefinition.EpicGameStoreBuildsIds.IsIn(version)) 
                            return version.EpicGameStoreBuilds.Select(build => (Version: version, Build: build));
                        return [];
                    }
                )
                .ToLookup(row => row.Build.ManifestHash);

            var builds = locatorIds
                .Select(locatorString => ManifestHash.FromUnsanitized(locatorString.Value))
                .SelectMany(manifestHash => versionsByManifestHash[manifestHash].Select(row => (row.Version, manifestHash)));

            if (builds.TryGetFirst(out var build))
            {
                versionDefinition = build.Version;
                score = 1;
                return true;
            }

            return false;
        }
        else
        {
            _logger.LogDebug("No way to get game version for: {Store}", gameStore);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryGetLocatorIdsForVanityVersion(GameStore gameStore, VanityVersion version, out LocatorId[] commonIds)
    {
        // Linux fork: search the shipped database first, then the writable local overlay. The returned
        // version definition is navigated within its own database, so resolving locator ids stays valid.
        foreach (var db in ReadDbs())
        {
            if (VersionDefinition.FindByName(db, version.Value).TryGetFirst(out var versionDef))
            {
                commonIds = GetLocatorIdsForVersionDefinition(gameStore, versionDef);
                return true;
            }
        }

        commonIds = [];
        return false;
    }

    public LocatorId[] GetLocatorIdsForVersionDefinition(GameStore gameStore, VersionDefinition.ReadOnly versionDefinition)
    {
        if (gameStore == GameStore.GOG)
        {
            return versionDefinition.GogBuilds.Select(build => LocatorId.From(build.BuildId!.Value.ToString())).ToArray();
        }

        if (gameStore == GameStore.Steam)
        {
            return versionDefinition.SteamManifests.Select(manifest => LocatorId.From(manifest.ManifestId.ToString())).ToArray();
        }
        
        if (gameStore == GameStore.EGS)
        {
            return versionDefinition.EpicGameStoreBuilds.Select(build => LocatorId.From(build.ManifestHash.Value)).ToArray();
        }

        throw new NotSupportedException("No way to get common IDs for: " + gameStore);
    }

    /// <inheritdoc />
    public Optional<VersionData> SuggestVersionData(GameInstallation gameInstallation, IEnumerable<(GamePath Path, Hash Hash)> files)
    {
        var filesSet = files.ToHashSet();

        List<(VersionData VersionData, int Matches)> versionMatches = [];
        foreach (var versionDefinition in GetVersionDefinitions(gameInstallation.Game.NexusModsGameId.Value))
        {
            var locatorIds = GetLocatorIdsForVersionDefinition(gameInstallation.LocatorResult.Store, versionDefinition);

            var matchingCount = GetGameFiles((gameInstallation.LocatorResult.Store, locatorIds))
                .Count(file => filesSet.Contains((file.Path, file.Hash)));

            versionMatches.Add((new VersionData(locatorIds, VanityVersion.From(versionDefinition.Name)), matchingCount));
        }

        return versionMatches
            .OrderByDescending(t => t.Matches)
            .Select(t => t.VersionData)
            .FirstOrOptional(_ => true);
    }

    /// <inheritdoc />
    public async Task AddLocalSteamVersionAsync(
        AppId appId,
        DepotId depotId,
        ManifestId manifestId,
        string versionName,
        NexusModsGameId? gameId,
        OperatingSystem operatingSystem,
        IReadOnlyList<(RelativePath Path, MultiHash Hash)> verifiedFiles,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync();

        var overlay = EnsureOverlayOpen();

        // Idempotency: a Steam manifest id uniquely identifies a specific depot content snapshot, so if
        // it is already present in the overlay the recorded data is identical. Skip to avoid stacking
        // duplicate manifests (and duplicate version definitions) on repeated recognition runs.
        if (SteamManifest.FindByManifestId(overlay.Connection.Db, manifestId).Any())
        {
            _logger.LogInformation("Local Steam manifest {ManifestId} is already recorded in the overlay; skipping", manifestId.Value);
            return;
        }

        using var tx = overlay.Connection.BeginTransaction();

        // Each verified file becomes a self-contained hash relation + path relation in the overlay, so
        // the overlay does not depend on entity ids from the shipped database.
        var pathIds = new List<EntityId>(verifiedFiles.Count);
        foreach (var (path, hash) in verifiedFiles)
        {
            var hashRelation = new HashRelation.New(tx)
            {
                XxHash3 = hash.XxHash3,
                XxHash64 = hash.XxHash64,
                MinimalHash = hash.MinimalHash,
                Md5 = hash.Md5,
                Sha1 = hash.Sha1,
                Crc32 = hash.Crc32,
                Size = hash.Size,
            };

            var pathRelation = new PathHashRelation.New(tx)
            {
                Path = path,
                HashId = hashRelation,
            };
            pathIds.Add(pathRelation.Id);
        }

        var manifest = new SteamManifest.New(tx)
        {
            AppId = appId,
            DepotId = depotId,
            ManifestId = manifestId,
            Name = versionName,
            FilesIds = pathIds,
        };

        if (gameId.HasValue)
        {
            var versionDefinition = new VersionDefinition.New(tx)
            {
                Name = versionName,
                OperatingSystem = operatingSystem,
                GameId = gameId.Value,
                GOG = [],
                Steam = [manifestId.Value.ToString()],
                EpicBuildIds = [],
            };
            tx.Add(versionDefinition, VersionDefinition.SteamManifestsIds, manifest.Id);
        }

        await tx.Commit();

        _logger.LogInformation(
            "Recorded local Steam version '{Version}' (app {AppId}, depot {DepotId}, manifest {ManifestId}) with {FileCount} verified files into the local hash overlay",
            versionName, appId.Value, depotId.Value, manifestId.Value, pathIds.Count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var connection in _databases.Values)
        {
            connection.Backend.Dispose();
            connection.Store.Dispose();
        }

        if (_overlayDb is not null)
        {
            _overlayDb.Store.Dispose();
            _overlayDb.Backend.Dispose();
            _overlayDb = null;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var existingDatabases = ExistingDBs().ToArray();

        // Cleanup old databases
        foreach (var databaseInfo in existingDatabases.Skip(1))
        {
            databaseInfo.Path.DeleteDirectory(true);
        }

        var forceUpdate = false;
        try
        {
            if (existingDatabases.TryGetFirst(out var latestDatabase))
            {
                _currentDb = OpenDb(latestDatabase);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to open latest database, forcing update");
            forceUpdate = true;
        }

        await CheckForUpdateCore(forceUpdate: forceUpdate, cancellationToken);

        // Linux fork: open the writable local overlay so locally-recognised versions are unioned into
        // read paths. Failure to open it must not prevent the shipped database from being used.
        try
        {
            EnsureOverlayOpen();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to open the writable local hash overlay; locally-recognised versions will be unavailable this session");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
