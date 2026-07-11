using DynamicData.Kernel;
using JetBrains.Annotations;
using Apocrypha.Abstractions.Games.FileHashes.Models;
using Apocrypha.Abstractions.Steam.Values;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Hashes;
using Apocrypha.Sdk.NexusModsApi;
using OperatingSystem = Apocrypha.Abstractions.Games.FileHashes.Values.OperatingSystem;

namespace Apocrypha.Abstractions.Games.FileHashes;

/// <summary>
/// Interface for the file hashes service, which provides a way to download and update the file hashes database
/// </summary>
[PublicAPI]
public interface IFileHashesService
{
    /// <summary>
    /// The current file hashes database, will throw an error if not initialized via GetFileHashesDb first.
    /// </summary>
    public IDb Current { get; }

    /// <summary>
    /// Get the file hashes database, downloading it if necessary
    /// </summary>
    public ValueTask<IDb> GetFileHashesDb();

    /// <summary>
    /// Force an update of the file hashes database
    /// </summary>
    public Task CheckForUpdate(bool forceUpdate = false);

    /// <summary>
    /// Gets all known vanity versions for a given game.
    /// </summary>
    public IEnumerable<VanityVersion> GetKnownVanityVersions(NexusModsGameId nexusModsGameId);

    /// <summary>
    /// Gets all game files associated with the provided locator IDs.
    /// </summary>
    public IEnumerable<GameFileRecord> GetGameFiles(LocatorIdsWithGameStore locatorIdsWithGameStore);

    /// <summary>
    /// Tries to get a vanity version based on the locator IDs.
    /// </summary>
    public bool TryGetVanityVersion(LocatorIdsWithGameStore locatorIdsWithGameStore, out VanityVersion version);

    /// <summary>
    /// Tries to get all locator IDs for the given store and vanity version.
    /// </summary>
    public bool TryGetLocatorIdsForVanityVersion(GameStore gameStore, VanityVersion version, out LocatorId[] locatorIds);

    /// <summary>
    /// Gets all locator IDs for a given store and <see cref="VersionDefinition"/>.
    /// </summary>
    public LocatorId[] GetLocatorIdsForVersionDefinition(GameStore gameStore, VersionDefinition.ReadOnly versionDefinition);

    /// <summary>
    /// Suggest version data for a given game installation and files.
    /// </summary>
    public Optional<VersionData> SuggestVersionData(GameInstallation gameInstallation, IEnumerable<(GamePath Path, Hash Hash)> files);

    /// <summary>
    /// Records a locally-recognised Steam game version in the writable local overlay database, so that
    /// subsequent lookups (<see cref="GetGameFiles"/>, version resolution) recognise an installed game
    /// whose version is not present in the shipped/embedded hash database.
    /// </summary>
    /// <remarks>
    /// Linux fork: this is the write side of the login-free, download-free local recognition pipeline.
    /// The overlay is unioned into all read paths, and the shipped database remains read-only. Only files
    /// whose contents were verified against the Steam depot manifest (SHA1 match) should be passed here.
    /// </remarks>
    /// <param name="appId">The Steam app id the manifest belongs to.</param>
    /// <param name="depotId">The Steam depot id of the manifest.</param>
    /// <param name="manifestId">The Steam manifest id; this is the locator id the synchronizer matches against.</param>
    /// <param name="versionName">A human-friendly version name (used for the manifest branch and the version definition).</param>
    /// <param name="gameId">When provided, a <see cref="VersionDefinition"/> for this game is also written and anchored to the manifest.</param>
    /// <param name="operatingSystem">The operating system the version definition applies to.</param>
    /// <param name="verifiedFiles">The verified-vanilla files (relative path + full hash set) to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AddLocalSteamVersionAsync(
        AppId appId,
        DepotId depotId,
        ManifestId manifestId,
        string versionName,
        NexusModsGameId? gameId,
        OperatingSystem operatingSystem,
        IReadOnlyList<(RelativePath Path, MultiHash Hash)> verifiedFiles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the local overlay already contains a Steam manifest with the given id, and —
    /// when <paramref name="gameId"/> is provided — whether a <see cref="VersionDefinition"/> for
    /// that game references it. Lets recognition skip re-hashing depots that are already recorded.
    /// </summary>
    /// <remarks>Linux fork: query side of the local recognition pipeline.</remarks>
    /// <returns>True when the manifest is recorded in the overlay.</returns>
    public bool TryGetLocalSteamManifestStatus(ManifestId manifestId, NexusModsGameId? gameId, out bool hasVersionDefinition);
}

/// <summary>
/// Tuple of many <see cref="LocatorId"/> and <see cref="Sdk.VanityVersion"/>.
/// </summary>
public record struct VersionData(LocatorId[] LocatorIds, VanityVersion VanityVersion);
