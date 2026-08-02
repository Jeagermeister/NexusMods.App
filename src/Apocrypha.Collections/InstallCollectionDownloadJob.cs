using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Collections.Types;
using Apocrypha.Abstractions.Collections.Json;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Games.FOMOD;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Networking.NexusWebApi;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Hashes;
using Apocrypha.Sdk.IO;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Apocrypha.Collections;
using CollectionMod = Mod;

[PublicAPI]
public class InstallCollectionDownloadJob : IJobDefinitionWithStart<InstallCollectionDownloadJob, LoadoutItemGroup.ReadOnly>
{
    public required ILogger Logger { get; init; }
    public required CollectionDownload.ReadOnly Item { get; init; }
    public required CollectionGroup.ReadOnly Group { get; init; }
    public required LoadoutId TargetLoadout { get; init; }
    public required CollectionMod CollectionMod { get; init; }
    public required NexusModsCollectionLibraryFile.ReadOnly SourceCollection { get; init; }
    private LibraryArchive.ReadOnly SourceCollectionArchive => SourceCollection.AsLibraryFile().ToLibraryArchive();

    public required IServiceProvider ServiceProvider { get; init; }
    public required IConnection Connection { get; init; }
    public required IFileStore FileStore { get; init; }
    public required ILibraryService LibraryService { get; init; }
    public required ILoadoutManager LoadoutManager { get; init; }

    public ILibraryItemInstaller? FallbackInstaller { get; init; }
    public Optional<GamePath> FallbackCollectionInstallDirectory { get; init; }

    public static async ValueTask<InstallCollectionDownloadJob> Create(
        IServiceProvider serviceProvider,
        LoadoutId targetLoadout,
        CollectionDownload.ReadOnly download,
        CancellationToken cancellationToken)
    {
        var connection = serviceProvider.GetRequiredService<IConnection>();

        var optionalCollectionGroup = CollectionDownloader.GetCollectionGroup(download.CollectionRevision, targetLoadout, connection.Db);
        if (!optionalCollectionGroup.HasValue) throw new InvalidOperationException("Collection must exist!");
        var collectionGroup = optionalCollectionGroup.Value.AsCollectionGroup();

        var sourceCollection = serviceProvider.GetRequiredService<CollectionDownloader>().GetLibraryFile(download.CollectionRevision);
        var nexusModsLibrary = serviceProvider.GetRequiredService<NexusModsLibrary>();

        var root = await nexusModsLibrary.ParseCollectionJsonFile(sourceCollection, cancellationToken);
        var collectionMod = root.Mods[download.ArrayIndex];

        return new InstallCollectionDownloadJob
        {
            Logger = serviceProvider.GetRequiredService<ILogger<InstallCollectionDownloadJob>>(),
            Item = download,
            CollectionMod = collectionMod,
            Group = collectionGroup,
            TargetLoadout = targetLoadout,
            SourceCollection = sourceCollection,

            ServiceProvider = serviceProvider,
            Connection = connection,
            FileStore = serviceProvider.GetRequiredService<IFileStore>(),
            LibraryService = serviceProvider.GetRequiredService<ILibraryService>(),
            LoadoutManager = serviceProvider.GetRequiredService<ILoadoutManager>(),
        };
    }

    /// <inheritdoc/>
    public async ValueTask<LoadoutItemGroup.ReadOnly> StartAsync(IJobContext<InstallCollectionDownloadJob> context)
    {
        var (group, patchedFiles) = await Install(context);

        // Everything from here on runs against an already-committed group. A failure would otherwise
        // strand it: installed, deployed, unpatched and (on the standard chain) untagged, yet still
        // reporting installed, so the job skips it on every retry (S5-1).
        try
        {
            using var tx = Connection.BeginTransaction();

            // Patch files
            foreach (var patchedFile in patchedFiles)
            {
                if (!group.Children.TryGetFirst(x => LoadoutFile.Hash.TryGetValue(x, out var hash) && hash == patchedFile.OriginalFileHashes.XxHash3, out var fileToPatch))
                {
                    Logger.LogWarning("Unable to find original file of patched file `{Path}` by hash `{OriginalHash}` in loadout group", patchedFile.FileName, patchedFile.OriginalFileHashes);
                    continue;
                }

                tx.Add(fileToPatch.Id, LoadoutFile.Hash, patchedFile.PatchedFileHashes.XxHash3);
                tx.Add(fileToPatch.Id, LoadoutFile.Size, patchedFile.PatchedFileHashes.Size);
            }

            // Add missing data from the collection to the item
            tx.Add(group.Id, LoadoutItem.Name, CollectionMod.Source.LogicalFilename ?? CollectionMod.Name);
            tx.Add(group.Id, NexusCollectionItemLoadoutGroup.Download, Item);
            tx.Add(group.Id, NexusCollectionItemLoadoutGroup.IsRequired, Item.IsRequired);

            var result = await tx.Commit();
            return new LoadoutItemGroup.ReadOnly(result.Db, group.Id);
        }
        catch (Exception exception)
        {
            await RetractStrandedGroup(group, exception);
            throw;
        }
    }

    /// <summary>
    /// Undoes an already-committed install whose follow-up work failed, so the download falls back to
    /// "in library" — a state a retry can actually heal. Never throws: the caller is already unwinding
    /// a failure and the original exception is the one worth surfacing.
    /// </summary>
    private async ValueTask RetractStrandedGroup(LoadoutItemGroup.ReadOnly group, Exception cause)
    {
        try
        {
            await CollectionDownloader.RetractStrandedItemGroup(Connection, group.Id);
            Logger.LogWarning(cause, "Install of collection mod `{Name}` failed after its group was committed; removed the partially installed group so the download can be retried", Item.Name);
        }
        catch (Exception retractException)
        {
            // Nothing better to do -- the group stays stranded and will read as installed. Log loudly
            // enough that the state is at least diagnosable from a bug report.
            Logger.LogError(retractException, "Failed to remove the partially installed group for collection mod `{Name}` after install failure; it will incorrectly report as installed and must be removed by hand", Item.Name);
        }
    }

    private async ValueTask<(LoadoutItemGroup.ReadOnly, PatchedFile[])> Install(IJobContext<InstallCollectionDownloadJob> context)
    {
        if (Item.TryGetAsCollectionDownloadBundled(out var bundledDownload))
        {
            return (await InstallBundledMod(bundledDownload), []);
        }

        PatchedFile[] patchedFiles = [];

        var libraryFile = GetLibraryFile(Item, Connection.Db);
        var hasPatches = CollectionMod.Patches.Count > 0;

        if (CollectionMod.Hashes.Length > 0)
        {
            // A replicated mod builds its file list out of the patched output itself, so it has to
            // be patched up front. Its paths come straight from the collection's hash list rather
            // than an installer, so matching against the raw archive is correct here.
            if (hasPatches)
            {
                if (!libraryFile.TryGetAsLibraryArchive(out var replicatedArchive)) throw new NotSupportedException("Expected library file to be an archive");
                patchedFiles = await PatchFilesFromArchive(replicatedArchive, cancellationToken: context.CancellationToken);
            }

            return (await InstallReplicatedMod(patchedFiles), []);
        }

        // Everything below installs first and patches afterwards. Patch keys in the collection
        // manifest are relative to the mod's *installed* root, whereas the raw archive still
        // carries whatever wrapper the author shipped -- a "ModName/" folder, a "Data/" prefix,
        // or FOMOD option directories like "00 Required/". Resolving patches against the archive
        // therefore misses every mod that isn't packaged flat, which aborts the whole mod install.
        // The manifest can record fomod choices for archives that contain no FOMOD at all, and a
        // FOMOD script can legitimately produce zero file copies for this setup. In both cases the
        // fomod path reports NotSupported and the mod must fall back to the standard installer
        // chain -- previously the empty group was committed anyway, yielding a mod that reads as
        // installed everywhere while deploying nothing, with no error in sight.
        async ValueTask<(LoadoutItemGroup.ReadOnly, PatchedFile[])> InstallViaStandardChain()
        {
            var result = await LoadoutManager.InstallItem(
                libraryFile.AsLibraryItem(),
                TargetLoadout,
                parent: Group.AsLoadoutItemGroup().LoadoutItemGroupId,
                // NOTE(erri120): https://github.com/Nexus-Mods/NexusMods.App/issues/2553
                // The advanced installer shouldn't appear when installing collections,
                // the decision was made that the app should behave similar to Vortex,
                // which installs unknown stuff into a "default folder"
                fallbackInstaller: FallbackInstaller
            );

            Debug.Assert(result.LoadoutItemGroup.HasValue);
            var installedGroup = result.LoadoutItemGroup!.Value;
            return (installedGroup, await PatchInstalledGroupOrRetract(installedGroup, hasPatches, context.CancellationToken));
        }

        if (CollectionMod.Choices is { Type: ChoicesType.fomod })
        {
            try
            {
                var fomodGroup = await InstallFomodWithPredefinedChoices(context.CancellationToken);
                return (fomodGroup, await PatchInstalledGroupOrRetract(fomodGroup, hasPatches, context.CancellationToken));
            }
            catch (FomodNotApplicableException e)
            {
                Logger.LogWarning("FOMOD install of `{Name}` is not applicable ({Reason}); falling back to the standard installer chain", Item.Name, e.Message);
                return await InstallViaStandardChain();
            }
        }

        // A FOMOD whose choices were never recorded by the curator must NOT fall through to the
        // game's installer chain: that pops the INTERACTIVE guided installer mid-unattended
        // collection install (CODE_REVIEW.md §7 #16). Route it through the preset path with an
        // empty preset instead — PresetGuidedInstaller degrades to the installer's own defaults
        // (Required + PreSelected options), keeping the install hands-off.
        if (libraryFile.TryGetAsLibraryArchive(out var maybeFomodArchive)
            && maybeFomodArchive.Children.Any(static c => c.Path.EndsWith(FomodConstants.XmlConfigName)))
        {
            Logger.LogInformation("Collection mod {Name} is a FOMOD without recorded choices; installing non-interactively with installer defaults", Item.Name);
            try
            {
                var defaultsGroup = await InstallFomodWithPredefinedChoices(context.CancellationToken);
                return (defaultsGroup, await PatchInstalledGroupOrRetract(defaultsGroup, hasPatches, context.CancellationToken));
            }
            catch (FomodNotApplicableException e)
            {
                Logger.LogWarning("FOMOD-with-defaults install of `{Name}` is not applicable ({Reason}); falling back to the standard installer chain", Item.Name, e.Message);
                return await InstallViaStandardChain();
            }
        }

        return await InstallViaStandardChain();
    }

    /// <summary>
    /// Thrown inside the fomod install transaction when the installer reports the archive cannot
    /// be handled as a FOMOD; aborts the transaction (so no empty group is committed) and signals
    /// the caller to use the standard installer chain instead.
    /// </summary>
    private sealed class FomodNotApplicableException(string reason) : Exception(reason);

    private Task<LoadoutItemGroup.ReadOnly> InstallBundledMod(CollectionDownloadBundled.ReadOnly download) => LoadoutManager.InstallItemWrapper(TargetLoadout, tx =>
    {
        // Bundled mods are found inside the collection archive, so we'll have to find the files that are prefixed with the mod's source file expression.
        var prefixPath = RelativePath.FromUnsanitizedInput("bundled").Join(download.BundledPath);
        var prefixFiles = SourceCollectionArchive.Children.Where(f => f.Path.InFolder(prefixPath)).ToArray();

        var modGroup = new NexusCollectionBundledLoadoutGroup.New(tx, out var id)
        {
            CollectionLibraryFileId = SourceCollection,
            BundleDownloadId = download,
            NexusCollectionItemLoadoutGroup = new NexusCollectionItemLoadoutGroup.New(tx, id)
            {
                IsRequired = download.AsCollectionDownload().IsRequired,
                DownloadId = download.AsCollectionDownload(),
                LoadoutItemGroup = new LoadoutItemGroup.New(tx, id)
                {
                    IsGroup = true,
                    LoadoutItem = new LoadoutItem.New(tx, id)
                    {
                        Name = download.AsCollectionDownload().Name,
                        LoadoutId = TargetLoadout,
                        ParentId = Group.Id,
                    },
                },
            },
        };

        // NOTE(erri120): for details see https://github.com/Nexus-Mods/NexusMods.App/issues/2630#issuecomment-2653787872
        var parentPath = FallbackCollectionInstallDirectory.ValueOr(() => new GamePath(LocationId.Game, ""));

        foreach (var file in prefixFiles)
        {
            // Remove the prefix path from the file path
            var fixedPath = file.Path.RelativeTo(prefixPath);

            // Fill out the rest of the file information
            _ = new LoadoutFile.New(tx, out var fileId)
            {
                Hash = file.AsLibraryFile().Hash,
                Size = file.AsLibraryFile().Size,
                LoadoutItemWithTargetPath = new LoadoutItemWithTargetPath.New(tx, fileId)
                {
                    // TargetPath.Item1 is the LOADOUT id -- it was `fileId` here (inherited from
                    // upstream), which made every replicated-install file invisible to any query
                    // filtering on TargetPath.Item1 (plugin/REDmod sort-order SQL). The
                    // synchronizer filters on the Loadout attribute, so the files still deployed:
                    // split-brain. _0010_FixCollectionTargetPaths repairs existing datastores.
                    TargetPath = (TargetLoadout.Value, parentPath.LocationId, parentPath.Path.Join(fixedPath)),
                    LoadoutItem = new LoadoutItem.New(tx, fileId)
                    {
                        Name = file.Path,
                        LoadoutId = TargetLoadout,
                        ParentId = modGroup.Id,
                    },
                },
            };
        }

        return Task.FromResult(LoadoutItemGroupId.From(id.Value));
    });

    /// <summary>
    /// This is how we should get a fomod installer. We don't want to get it from DI or new it up, because
    /// the game may set custom target folders or other settings. So we'll have to get the game instance
    /// and find the installer that matches the type 
    /// </summary>
    private FomodXmlInstaller GetFomodXmlInstaller(CancellationToken cancellationToken)
    {
        var loadout = Loadout.Load(Connection.Db, TargetLoadout);
        var game = loadout.InstallationInstance.GetGame();
        var installer = game.LibraryItemInstallers.OfType<FomodXmlInstaller>().FirstOrDefault();
        
        return installer ?? throw new InvalidOperationException("FomodXmlInstaller not found");
    }

    /// <summary>
    /// Install a fomod with predefined choices.
    /// </summary>
    private Task<LoadoutItemGroup.ReadOnly> InstallFomodWithPredefinedChoices(CancellationToken cancellationToken) => LoadoutManager.InstallItemWrapper(TargetLoadout, async tx =>
    {
        var libraryFile = GetLibraryFile(Item, Connection.Db);
        if (!libraryFile.TryGetAsLibraryArchive(out var libraryArchive)) throw new NotSupportedException();

        var fomodInstaller = GetFomodXmlInstaller(cancellationToken);

        var loadoutItemGroup = new LoadoutItemGroup.New(tx, out var id)
        {
            IsGroup = true,
            LoadoutItem = new LoadoutItem.New(tx, id)
            {
                Name = Item.Name,
                LoadoutId = TargetLoadout,
                ParentId = Group.Id,
            },
        };

        var nexusCollectionItemLoadoutGroup = new NexusCollectionItemLoadoutGroup.New(tx, id)
        {
            DownloadId = Item,
            IsRequired = Item.IsRequired,
            LoadoutItemGroup = loadoutItemGroup,
        };
        
        _ = new LibraryLinkedLoadoutItem.New(tx, id)
        {
            LibraryItemId = libraryFile.AsLibraryItem(),
            LoadoutItemGroup = nexusCollectionItemLoadoutGroup.GetLoadoutItemGroup(tx),
        };

        var loadout = new Loadout.ReadOnly(Connection.Db, TargetLoadout);

        // Choices may legitimately be null here (the defaults route above): an empty preset makes
        // PresetGuidedInstaller emit the installer's default selections for every step.
        var options = CollectionMod.Choices?.Options ?? [];
        var installerResult = await fomodInstaller.ExecuteAsync(libraryArchive, loadoutItemGroup, tx, loadout, options, cancellationToken: cancellationToken);

        // Throwing here aborts the wrapper's transaction, so the group above is never committed.
        if (installerResult.IsNotSupported(out var notSupportedReason))
            throw new FomodNotApplicableException(notSupportedReason ?? "not supported");

        return loadoutItemGroup;
    });

    /// <summary>
    /// This sort of install is a bit strange. The Hashes field contains pairs of MD5 hashes and paths. The paths are
    /// the target locations of the mod files. The MD5 hashes are the hashes of the files. So it's a fromHash->toPath
    /// situation. We don't store the MD5 hashes in the database, so we'll have to calculate them on the fly.
    /// </summary>
    /// <param name="patchedFiles"></param>
    private Task<LoadoutItemGroup.ReadOnly> InstallReplicatedMod(PatchedFile[] patchedFiles) => LoadoutManager.InstallItemWrapper(TargetLoadout, async tx =>
    {
        // So collections hash everything by MD5, so we'll have to collect MD5 information for the files in the archive.
        // We don't do this during indexing into the library because this is the only case where we need MD5 hashes.
        ConcurrentDictionary<Md5Value, HashMapping> hashes = new();

        var libraryFile = GetLibraryFile(Item, Connection.Db);
        if (!libraryFile.TryGetAsLibraryArchive(out var libraryArchive))
            throw new NotSupportedException("Expected library file to be an archive");

        await Parallel.ForEachAsync(libraryArchive.Children, async (child, token) =>
        {
            await using var stream = await FileStore.GetFileStream(child.AsLibraryFile().Hash, token);
            var md5 = await Md5Hasher.HashAsync(stream, cancellationToken: token);

            var file = child.AsLibraryFile();
            hashes[md5] = new HashMapping()
            {
                Hash = file.Hash,
                Size = file.Size,
            };
        });

        foreach (var patchedFile in patchedFiles)
        {
            if (!hashes.ContainsKey(patchedFile.OriginalFileHashes.Md5))
            {
                Logger.LogWarning("Archive doesn't contain a file matching the MD5 hash {MD5Hash} of a file to patch", patchedFile.OriginalFileHashes.Md5);
                continue;
            }

            var hashMapping = new HashMapping
            {
                Hash = patchedFile.PatchedFileHashes.XxHash3,
                Size = patchedFile.PatchedFileHashes.Size,
            };

            hashes[patchedFile.PatchedFileHashes.Md5] = hashMapping;
        }

        var group = new NexusCollectionReplicatedLoadoutGroup.New(tx, out var id)
        {
            IsReplicated = true,
            NexusCollectionItemLoadoutGroup = new NexusCollectionItemLoadoutGroup.New(tx, id)
            {
                DownloadId = Item,
                IsRequired = Item.IsRequired,
                LoadoutItemGroup = new LoadoutItemGroup.New(tx, id)
                {
                    IsGroup = true,
                    LoadoutItem = new LoadoutItem.New(tx, id)
                    {
                        Name = Item.Name,
                        LoadoutId = TargetLoadout,
                        ParentId = Group.Id,
                    },
                },
            },
        };

        _ = new LibraryLinkedLoadoutItem.New(tx, id)
        {
            LibraryItemId = libraryFile.AsLibraryItem(),
            LoadoutItemGroup = group.GetNexusCollectionItemLoadoutGroup(tx).GetLoadoutItemGroup(tx),
        };

        // Replicated paths in the manifest are relative to the game's mod install directory
        // (for Creation Engine games that is `Data`), matching how Vortex deploys them. Mapping
        // them onto the game root instead puts plugins where the engine never looks -- the mod
        // reports as installed while every plugin that masters it fails with a missing master.
        var replicatedRoot = FallbackCollectionInstallDirectory;

        // Now we map the files to their locations based on the hashes
        foreach (var pair in CollectionMod.Hashes)
        {
            // Try and find the hash we are looking for
            if (!hashes.TryGetValue(pair.MD5, out var libraryItem))
                throw new InvalidOperationException("The hash was not found in the archive.");

            var targetPath = replicatedRoot.HasValue
                ? new GamePath(replicatedRoot.Value.LocationId, replicatedRoot.Value.Path.Join(pair.Path))
                : new GamePath(LocationId.Game, pair.Path);

            // Map the file to the specific path
            _ = new LoadoutFile.New(tx, out var fileId)
            {
                Hash = libraryItem.Hash,
                Size = libraryItem.Size,
                LoadoutItemWithTargetPath = new LoadoutItemWithTargetPath.New(tx, fileId)
                {
                    // Same fix as InstallReplicatedMod above: loadout id, not the file's own id.
                    TargetPath = (TargetLoadout.Value, targetPath.LocationId, targetPath.Path),
                    LoadoutItem = new LoadoutItem.New(tx, fileId)
                    {
                        Name = pair.Path,
                        LoadoutId = TargetLoadout,
                        ParentId = group.Id,
                    },
                },
            };
        }

        return group.Id;
    });

    /// <summary>
    /// <see cref="PatchInstalledGroup"/>, but the group it is handed is already committed, so a patch
    /// failure would strand it (S5-1). Removes the group before letting the failure propagate.
    /// </summary>
    private async ValueTask<PatchedFile[]> PatchInstalledGroupOrRetract(LoadoutItemGroup.ReadOnly group, bool hasPatches, CancellationToken cancellationToken)
    {
        try
        {
            return await PatchInstalledGroup(group, hasPatches, cancellationToken);
        }
        catch (Exception exception)
        {
            await RetractStrandedGroup(group, exception);
            throw;
        }
    }

    /// <summary>
    /// Patches a mod that has already been installed, resolving patch keys against the installed
    /// file layout rather than the raw archive.
    /// </summary>
    private async ValueTask<PatchedFile[]> PatchInstalledGroup(LoadoutItemGroup.ReadOnly group, bool hasPatches, CancellationToken cancellationToken)
    {
        if (!hasPatches) return [];

        var srcFiles = new Dictionary<RelativePath, (Hash Hash, Size Size)>();
        foreach (var child in group.Children)
        {
            if (!LoadoutItemWithTargetPath.TargetPath.TryGetValue(child, out var rawTargetPath)) continue;
            if (!LoadoutFile.Hash.TryGetValue(child, out var hash)) continue;
            if (!LoadoutFile.Size.TryGetValue(child, out var size)) continue;

            GamePath targetPath = rawTargetPath;
            var path = targetPath.Path;
            srcFiles.TryAdd(path, (hash, size));

            // Patch keys are relative to the root the installer deployed into (for Creation Engine
            // games that is "Data/"), so index the path with its first segment removed as well.
            var withoutRoot = path.DropFirst();
            if (withoutRoot != default) srcFiles.TryAdd(withoutRoot, (hash, size));
        }

        return await PatchFilesCore(srcFiles, cancellationToken);
    }

    private async ValueTask<PatchedFile[]> PatchFilesFromArchive(LibraryArchive.ReadOnly srcArchive, CancellationToken cancellationToken)
    {
        var srcFiles = new Dictionary<RelativePath, (Hash Hash, Size Size)>();
        foreach (var child in srcArchive.Children)
        {
            var file = child.AsLibraryFile();
            srcFiles.TryAdd(child.Path, (file.Hash, file.Size));
        }

        return await PatchFilesCore(srcFiles, cancellationToken);
    }

    private async ValueTask<PatchedFile[]> PatchFilesCore(Dictionary<RelativePath, (Hash Hash, Size Size)> srcFiles, CancellationToken cancellationToken)
    {
        var collectionFiles = SourceCollectionArchive.Children.ToFrozenDictionary(static x => x.Path, static x => x.AsLibraryFile());

        var patches = CollectionMod.Patches.ToArray();
        var results = new ValueTuple<PatchedFile, MemoryStream>[patches.Length];

        await Parallel.ForAsync(fromInclusive: 0, toExclusive: patches.Length, cancellationToken: cancellationToken, async (i, innerCancellationToken) =>
        {
            var patch = patches[i];
            var result = await PatchFile(patch.Key, patch.Value, srcFiles, collectionFiles, cancellationToken: innerCancellationToken);
            results[i] = result;
        });

        var archivedFileEntries = results.Select(static tuple => new ArchivedFileEntry(
            StreamFactory: new MemoryStreamFactory(name: tuple.Item1.FileName, stream: tuple.Item2),
            Hash: tuple.Item1.PatchedFileHashes.XxHash3,
            Size: tuple.Item1.PatchedFileHashes.Size
        )).ToArray();

        await FileStore.BackupFiles(archivedFileEntries, deduplicate: true, token: cancellationToken);

        if (ApplicationConstants.IsDebug)
        {
            foreach (var result in results)
            {
                var (patchedFile, _) = result;
                var hash = patchedFile.PatchedFileHashes.XxHash3;
                var hasFile = await FileStore.HaveFile(hash);
                Debug.Assert(hasFile, "expected the file store to have the file it just backed up...");
            }
        }

        return results.Select(static x => x.Item1).ToArray();
    }

    private record struct PatchedFile(RelativePath FileName, MultiHash PatchedFileHashes, MultiHash OriginalFileHashes);

    private async ValueTask<(PatchedFile, MemoryStream)> PatchFile(
        RelativePath srcPath,
        Crc32Value expectedHash,
        Dictionary<RelativePath, (Hash Hash, Size Size)> srcFiles,
        FrozenDictionary<RelativePath, LibraryFile.ReadOnly> collectionFiles,
        CancellationToken cancellationToken)
    {
        // Patch keys come from a Windows-authored manifest and can use backslashes.
        var normalizedPath = RelativePath.FromUnsanitizedInput(srcPath.ToString());
        if (!srcFiles.TryGetValue(srcPath, out var srcFile) && !srcFiles.TryGetValue(normalizedPath, out srcFile))
            throw new KeyNotFoundException($"Collection download archive doesn't contain file `{srcPath}`");

        var patchName = RelativePath.FromUnsanitizedInput($"patches/{CollectionMod.Name}/{srcPath}.diff");
        if (!collectionFiles.TryGetValue(patchName, out var patchFile)) throw new KeyNotFoundException($"Collection archive doesn't contain file `{patchName}`");

        var patchedFileStream = new MemoryStream(capacity: (int)srcFile.Size.Value);
        var (originalFileHashes, patchedFileHashes) = await PatchFile(fileToPatchHash: srcFile.Hash, patchDataFile: patchFile, expectedHash: expectedHash, outputStream: patchedFileStream, cancellationToken: cancellationToken);

        var patchedFile = new PatchedFile(
            FileName: srcPath,
            PatchedFileHashes: patchedFileHashes,
            OriginalFileHashes: originalFileHashes
        );

        Debug.Assert(Size.FromLong(patchedFileStream.Length) == patchedFileHashes.Size.Value);
        Logger.LogDebug("Patching result: `{PatchedFile}`", patchedFile);

        patchedFileStream.Position = 0;
        return (patchedFile, patchedFileStream);
    }

    private async ValueTask<(MultiHash OriginalFileHashes, MultiHash PatchedFileHashes)> PatchFile(Hash fileToPatchHash, LibraryFile.ReadOnly patchDataFile, Crc32Value expectedHash, Stream outputStream, CancellationToken cancellationToken)
    {
        await using var inputStream = await FileStore.GetFileStream(fileToPatchHash, token: cancellationToken);

        var originalFileHashes = await MultiHasher.HashStream(inputStream, cancellationToken: cancellationToken);
        if (originalFileHashes.Crc32 != expectedHash.Value) throw new InvalidOperationException("The source file's CRC32 hash does not match the expected hash.");

        inputStream.Position = 0;

        var patchData = await FileStore.Load(patchDataFile.Hash, token: cancellationToken);
        PatchFile(inputStream, patchData, outputStream);

        outputStream.Position = 0;
        var patchedFileHashes = await MultiHasher.HashStream(outputStream, cancellationToken: cancellationToken);

        return (originalFileHashes, patchedFileHashes);
    }

    private static void PatchFile(Stream inputStream, byte[] patchData, Stream outputStream)
    {
        // NOTE(erri120): This patching library is kinda ass, the API isn't async, they create this memory stream multiple times, and generally allocate a bunch of memory.
        // I wouldn't be surprised if this line will show up in memory and performance diagnosers.
        BsDiff.BinaryPatch.Apply(inputStream, openPatchStream: () => new MemoryStream(patchData, writable: false), outputStream);
    }

    private LibraryFile.ReadOnly GetLibraryFile(CollectionDownload.ReadOnly download, IDb db)
    {
        var status = CollectionDownloader.GetStatus(download, Group, db);
        if (status.IsInLibrary(out var libraryItem)) return GetLibraryFile(libraryItem, download);

        if (status.IsInstalled(out var loadoutItem))
        {
            var libraryLinkedLoadoutItem = LibraryLinkedLoadoutItem.Load(loadoutItem.Db, loadoutItem.Id);
            if (!libraryLinkedLoadoutItem.IsValid()) throw new NotSupportedException($"Expected loadout item `{loadoutItem.Name}` for download `{download.Name}` (index={download.ArrayIndex}) to be linked to a library item");
            return GetLibraryFile(libraryLinkedLoadoutItem.LibraryItem, download);
        }

        throw new NotSupportedException($"Status for download `{download.Name}` (index={download.ArrayIndex}) is {status.Value.Index}");
    }

    private static LibraryFile.ReadOnly GetLibraryFile(LibraryItem.ReadOnly libraryItem, CollectionDownload.ReadOnly download)
    {
        if (!libraryItem.TryGetAsLibraryFile(out var libraryFile))
            throw new NotSupportedException($"Expected library item `{libraryItem.Name}` for download `{download.Name}` (index={download.ArrayIndex}) to be a library file");
        return libraryFile;
    }
}
