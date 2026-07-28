using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Collections.Json;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using Apocrypha.Networking.NexusWebApi;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Collections;

using ModAndDownload = (Mod Mod, CollectionDownload.ReadOnly Download);

/// <summary>
/// Job for installing a collection.
/// </summary>
public class InstallCollectionJob : IJobDefinitionWithStart<InstallCollectionJob, NexusCollectionLoadoutGroup.ReadOnly>
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public required NexusModsCollectionLibraryFile.ReadOnly SourceCollection { get; init; }
    public required CollectionRevisionMetadata.ReadOnly RevisionMetadata { get; init; }
    public required CollectionDownload.ReadOnly[] Items { get; init; }
    public required Optional<NexusCollectionLoadoutGroup.ReadOnly> Group { get; init; }

    public required IServiceProvider ServiceProvider { get; init; }
    public required IFileStore FileStore { get; init; }
    public required ILibraryService LibraryService { get; init; }
    public required ILoadoutManager LoadoutManager { get; init; }
    public required IConnection Connection { get; init; }
    public required LoadoutId TargetLoadout { get; init; }
    public required NexusModsLibrary NexusModsLibrary { get; init; }
    public required ILogger Logger { get; init; }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

    /// <summary>
    /// Factory.
    /// </summary>
    public static IJobTask<InstallCollectionJob, NexusCollectionLoadoutGroup.ReadOnly> Create(
        IServiceProvider provider,
        LoadoutId target,
        NexusModsCollectionLibraryFile.ReadOnly source,
        CollectionRevisionMetadata.ReadOnly revisionMetadata,
        CollectionDownload.ReadOnly[] items)
    {
        var connection = provider.GetRequiredService<IConnection>();
        var group = CollectionDownloader.GetCollectionGroup(revisionMetadata, target, connection.Db);

        var monitor = provider.GetRequiredService<IJobMonitor>();

        var job = new InstallCollectionJob
        {
            Group = group,
            Items = items,
            TargetLoadout = target,
            SourceCollection = source,
            RevisionMetadata = revisionMetadata,
            ServiceProvider = provider,
            Connection = connection,
            FileStore = provider.GetRequiredService<IFileStore>(),
            LibraryService = provider.GetRequiredService<ILibraryService>(),
            LoadoutManager = provider.GetRequiredService<ILoadoutManager>(),
            NexusModsLibrary = provider.GetRequiredService<NexusModsLibrary>(),
            Logger = provider.GetRequiredService<ILogger<InstallCollectionJob>>(),
        };

        return monitor.Begin<InstallCollectionJob, NexusCollectionLoadoutGroup.ReadOnly>(job);
    }

    /// <summary>
    /// Installs the collection.
    /// </summary>
    public async ValueTask<NexusCollectionLoadoutGroup.ReadOnly> StartAsync(IJobContext<InstallCollectionJob> context)
    {
        Logger.LogInformation("Starting installation of `{CollectionName}/{RevisionNumber}`", RevisionMetadata.Collection.Name, RevisionMetadata.RevisionNumber);

        var g = Group.Convert(static x => x.AsCollectionGroup());
        var items = Items
            .Where(item => !CollectionDownloader.GetStatus(item, g, Connection.Db).IsInstalled(out _))
            .ToArray();

        var skipCount = Items.Length - items.Length;
        if (skipCount > 0) Logger.LogInformation("Skipping `{Count}` already installed items for `{CollectionName}/{RevisionNumber}`", skipCount, RevisionMetadata.Collection.Name, RevisionMetadata.RevisionNumber);

        var isFullyDownloaded = CollectionDownloader.IsFullyDownloaded(items, db: Connection.Db);
        if (!isFullyDownloaded) throw new InvalidOperationException("The collection hasn't fully been downloaded!");

        var root = await NexusModsLibrary.ParseCollectionJsonFile(SourceCollection, context.CancellationToken);
        var modsAndDownloads = GatherDownloads(items, root);

        NexusCollectionLoadoutGroup.ReadOnly collectionGroup;
        if (Group.HasValue)
        {
            collectionGroup = Group.Value;
        }
        else
        {
            using var tx = Connection.BeginTransaction();
            var group = new NexusCollectionLoadoutGroup.New(tx, out var id)
            {
                CollectionId = RevisionMetadata.Collection,
                RevisionId = RevisionMetadata,
                LibraryFileId = SourceCollection,
                CollectionGroup = new CollectionGroup.New(tx, id)
                {
                    IsReadOnly = true,
                    LoadoutItemGroup = new LoadoutItemGroup.New(tx, id)
                    {
                        IsGroup = true,
                        LoadoutItem = new LoadoutItem.New(tx, id)
                        {
                            Name = RevisionMetadata.Collection.Name,
                            LoadoutId = TargetLoadout,
                            IsDisabled = true,
                        },
                    },
                },
            };

            var groupResult = await tx.Commit();
            collectionGroup = groupResult.Remap(group);
        }

        var loadout = Loadout.Load(Connection.Db, TargetLoadout);
        var game = (loadout.InstallationInstance.Game as IGame)!;
        var fallbackInstaller = FallbackCollectionDownloadInstaller.Create(ServiceProvider, loadout, game);

        await Parallel.ForEachAsync(modsAndDownloads, context.CancellationToken, async (modAndDownload, _) =>
        {
            try
            {
                Logger.LogDebug("Installing `{DownloadName}` (index={Index}) into `{CollectionName}/{RevisionNumber}`", modAndDownload.Mod.Name, modAndDownload.Download.ArrayIndex, RevisionMetadata.Collection.Name, RevisionMetadata.RevisionNumber);
                await InstallMod(modAndDownload, collectionGroup, fallbackInstaller, game.GetFallbackCollectionInstallDirectory(loadout.InstallationInstance));
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to install `{DownloadName}` (index={Index}) into `{CollectionName}/{RevisionNumber}`", modAndDownload.Mod.Name, modAndDownload.Download.ArrayIndex, RevisionMetadata.Collection.Name, RevisionMetadata.RevisionNumber);
            }
        });

        await ApplyCuratedPluginState(root, collectionGroup);
        await SeedCuratedLoadOrder(root, collectionGroup, context.CancellationToken);

        var allRequiredItems = CollectionDownloader.GetItems(RevisionMetadata, CollectionDownloader.ItemType.Required);
        var allRequiredItemsInstalled = allRequiredItems.All(item => CollectionDownloader
            .GetStatus(item, collectionGroup.AsCollectionGroup(), db: Connection.Db)
            .IsInstalled(out _));
        // Scoped block so the transaction disposes before the reloaded group is returned.
        {
            await LoadoutManager.ApplyCollectionDownloadRules(collectionGroup);

            using var tx = Connection.BeginTransaction();

            if (allRequiredItemsInstalled)
            {
                tx.Retract(collectionGroup.Id, LoadoutItem.Disabled, Null.Instance);
            }
            else
            {
                tx.Add(collectionGroup.Id, LoadoutItem.Disabled, Null.Instance);
            }

            var result = await tx.Commit();
            collectionGroup = NexusCollectionLoadoutGroup.Load(result.Db, collectionGroup.Id);
        }

        return collectionGroup;
    }

    private IJobTask<InstallCollectionDownloadJob, LoadoutItemGroup.ReadOnly> InstallMod(
        ModAndDownload modAndDownload,
        NexusCollectionLoadoutGroup.ReadOnly collectionGroup,
        ILibraryItemInstaller? fallbackInstaller,
        Optional<GamePath> fallbackCollectionInstallDirectory)
    {
        var monitor = ServiceProvider.GetRequiredService<IJobMonitor>();

        var job = new InstallCollectionDownloadJob
        {
            Logger = ServiceProvider.GetRequiredService<ILogger<InstallCollectionDownloadJob>>(),
            Item = modAndDownload.Download,
            CollectionMod = modAndDownload.Mod,
            Group = collectionGroup.AsCollectionGroup(),
            TargetLoadout = TargetLoadout,
            SourceCollection = SourceCollection,

            ServiceProvider = ServiceProvider,
            Connection = Connection,
            FileStore = FileStore,
            LibraryService = LibraryService,
            LoadoutManager = LoadoutManager,

            FallbackInstaller = fallbackInstaller,
            FallbackCollectionInstallDirectory = fallbackCollectionInstallDirectory,
        };

        return monitor.Begin<InstallCollectionDownloadJob, LoadoutItemGroup.ReadOnly>(job);
    }

    /// <summary>
    /// Disables the plugins the install produced that the curator does not run.
    /// </summary>
    /// <remarks>
    /// The manifest's `plugins` array is the set of plugins the curator actually loads -- Vortex
    /// records the enabled set, not the disabled one. An install can legitimately produce more
    /// than that (FOMOD options resolved differently than the curator's setup, patch packs that
    /// bundle a plugin per supported mod), and each extra plugin referencing a mod the collection
    /// does not include is a missing master, which takes the game down on startup with no
    /// indication of why. Only Gamebryo collections carry `plugins`; it is empty for every other
    /// game, so this is a no-op there.
    /// </remarks>
    private async ValueTask ApplyCuratedPluginState(CollectionRoot root, NexusCollectionLoadoutGroup.ReadOnly collectionGroup)
    {
        var curatorPlugins = CollectionPluginWhitelist.CuratorEnabledPlugins(root);

        // No plugin list means no curated state to enforce -- never treat that as "disable all".
        if (curatorPlugins.Count == 0) return;

        using var tx = Connection.BeginTransaction();
        var disabledCount = CollectionPluginWhitelist.DisableExtraPlugins(Connection.Db, tx, curatorPlugins, TargetLoadout, collectionGroup.Id);
        if (disabledCount == 0) return;

        await tx.Commit();
        Logger.LogInformation("Disabled {Count} plugin(s) the install produced that the curator does not run", disabledCount);
    }

    /// <summary>
    /// Seeds the game's load order from the collection's curated ordering data, where the game
    /// registers a seeder for it. A failure here degrades to the derived order the app produced
    /// before seeding existed — it must never fail the install.
    /// </summary>
    private async ValueTask SeedCuratedLoadOrder(CollectionRoot root, NexusCollectionLoadoutGroup.ReadOnly collectionGroup, CancellationToken token)
    {
        var loadout = Loadout.Load(Connection.Db, TargetLoadout);
        var gameId = loadout.InstallationInstance.Game.GameId;

        foreach (var seeder in ServiceProvider.GetServices<ICollectionLoadOrderSeeder>())
        {
            if (!seeder.GameIds.Contains(gameId)) continue;

            try
            {
                await seeder.SeedAsync(root, TargetLoadout, collectionGroup.AsCollectionGroup().CollectionGroupId, token);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to seed the curated load order for `{CollectionName}/{RevisionNumber}`", RevisionMetadata.Collection.Name, RevisionMetadata.RevisionNumber);
            }
        }
    }

    // Duplicated in NexusModsLibrary.Collections.cs (the projects sit on opposite sides of
    // the Networking reference); keep the two copies in sync.
    private static List<ModAndDownload> GatherDownloads(CollectionDownload.ReadOnly[] items, CollectionRoot root)
    {
        var map = items.ToDictionary(static download => download.ArrayIndex, static download => download);
        var list = new List<ModAndDownload>();

        foreach (var kv in map)
        {
            var (index, download) = kv;
            var mod = root.Mods[index];

            list.Add((mod, download));
        }

        return list;
    }
}
