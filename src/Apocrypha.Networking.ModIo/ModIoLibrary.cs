using Apocrypha.Sdk.Hashes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModIo.DTOs;
using Apocrypha.Abstractions.ModIo.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Networking.ModIo;

/// <summary>
/// Implementation of <see cref="IModIoLibrary"/> — the mod.io peer of the Thunderstore
/// library facade.
/// </summary>
public class ModIoLibrary : IModIoLibrary
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModIoLibrary> _logger;
    private readonly IConnection _connection;
    private readonly IModIoApiClient _apiClient;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ModIoLibrary(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<ModIoLibrary>>();
        _connection = serviceProvider.GetRequiredService<IConnection>();
        _apiClient = serviceProvider.GetRequiredService<IModIoApiClient>();
    }

    /// <inheritdoc/>
    public async Task<ModIoFileMetadata.ReadOnly> ResolveLatestFile(string gameNameId, string modNameId, CancellationToken cancellationToken = default)
    {
        var game = await _apiClient.GetGameByNameId(gameNameId, cancellationToken);
        if (game is null) throw new KeyNotFoundException($"Game `{gameNameId}` was not found on mod.io");

        var mod = await _apiClient.GetModByNameId(game.Id, modNameId, cancellationToken);
        if (mod is null) throw new KeyNotFoundException($"Mod `{modNameId}` was not found on mod.io under `{gameNameId}`");
        if (mod.Modfile is null) throw new KeyNotFoundException($"Mod `{mod.Name}` has no released file on mod.io");

        return await GetOrAddFile(game, mod, mod.Modfile, cancellationToken);
    }

    /// <inheritdoc/>
    public bool IsAlreadyDownloaded(ulong fileId, IDb? db = null)
    {
        db ??= _connection.Db;
        if (!TryFindFile(db, fileId, out var metadata)) return false;
        return metadata.LibraryItems.Any();
    }

    /// <inheritdoc/>
    public async Task<IJobTask<IModIoDownloadJob, AbsolutePath>> CreateDownloadJob(
        ModIoFileMetadata.ReadOnly fileMetadata,
        CancellationToken cancellationToken = default)
    {
        // binary URLs expire, so fetch a fresh one at download time
        var mod = await _apiClient.GetMod(fileMetadata.Mod.GameId, fileMetadata.Mod.ModId, cancellationToken);
        if (mod?.Modfile is null) throw new KeyNotFoundException($"Mod `{fileMetadata.Mod.Name}` is no longer available on mod.io");

        // the latest modfile may have moved on since we stored ours; download what the API
        // offers now and record it if it's new
        var file = mod.Modfile.Id == fileMetadata.FileId
            ? fileMetadata
            : await GetOrAddFile(game: null, mod, mod.Modfile, cancellationToken);

        if (!Uri.TryCreate(mod.Modfile.Download.BinaryUrl, UriKind.Absolute, out var binaryUri))
            throw new InvalidOperationException($"mod.io returned an invalid download URL for `{mod.Name}`");

        Md5Value? expectedMd5 = null;
        if (mod.Modfile.Filehash?.Md5 is { Length: 32 } md5Hex)
        {
            try { expectedMd5 = Md5Value.FromHex(md5Hex); }
            catch (Exception) { _logger.LogWarning("mod.io returned an unparsable md5 `{Md5}` for `{Mod}`; skipping integrity verification", md5Hex, mod.Name); }
        }

        _logger.LogInformation("Starting mod.io download of `{Mod}` (file {FileId})", mod.Name, mod.Modfile.Id);
        return ModIoDownloadJob.Create(_serviceProvider, file, binaryUri, expectedMd5);
    }

    private async Task<ModIoFileMetadata.ReadOnly> GetOrAddFile(GameDto? game, ModDto mod, ModfileDto modfile, CancellationToken cancellationToken)
    {
        var db = _connection.Db;
        if (TryFindFile(db, modfile.Id, out var existing)) return existing;

        using var tx = _connection.BeginTransaction();

        EntityId modEntityId;
        if (TryFindMod(db, mod.Id, out var existingMod))
        {
            modEntityId = existingMod.Id;
        }
        else
        {
            // resolve the game slug when the caller didn't already have it (metadata-only path)
            var gameNameId = game?.NameId;
            if (gameNameId is null)
            {
                _ = cancellationToken; // game lookups by numeric id aren't exposed; slug comes from the resolve path
                throw new InvalidOperationException($"Cannot create metadata for mod `{mod.Name}` without its game");
            }

            var newMod = new ModIoModMetadata.New(tx)
            {
                ModId = mod.Id,
                NameId = mod.NameId,
                GameNameId = gameNameId,
                GameId = mod.GameId,
                Name = mod.Name,
                ProfileUri = Uri.TryCreate(mod.ProfileUrl, UriKind.Absolute, out var profileUri)
                    ? profileUri
                    : ModIoUrls.GetModPageUri(gameNameId, mod.NameId),
            };

            var logo = mod.Logo?.Thumb320X180 ?? mod.Logo?.Original;
            if (Uri.TryCreate(logo, UriKind.Absolute, out var logoUri))
                newMod.LogoUri = logoUri;

            modEntityId = newMod.Id;
        }

        var newFile = new ModIoFileMetadata.New(tx)
        {
            FileId = modfile.Id,
            FileName = modfile.Filename,
            ModId = modEntityId,
        };

        if (!string.IsNullOrWhiteSpace(modfile.Version)) newFile.Version = modfile.Version;
        if (modfile.Filesize > 0) newFile.Size = Size.FromLong((long)modfile.Filesize);
        if (modfile.DateAdded > 0) newFile.UploadedAt = DateTimeOffset.FromUnixTimeSeconds(modfile.DateAdded).UtcDateTime;

        var txResults = await tx.Commit();
        return txResults.Remap(newFile);
    }

    private static bool TryFindFile(IDb db, ulong fileId, out ModIoFileMetadata.ReadOnly result)
    {
        foreach (var entity in ModIoFileMetadata.FindByFileId(db, fileId))
        {
            result = entity;
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryFindMod(IDb db, ulong modId, out ModIoModMetadata.ReadOnly result)
    {
        foreach (var entity in ModIoModMetadata.FindByModId(db, modId))
        {
            result = entity;
            return true;
        }

        result = default;
        return false;
    }
}
