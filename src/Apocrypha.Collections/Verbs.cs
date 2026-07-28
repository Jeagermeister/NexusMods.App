using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Cli;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Abstractions.NexusWebApi.Types;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using Apocrypha.Networking.NexusWebApi;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.ProxyConsole;

namespace Apocrypha.Collections;

internal static class Verbs
{
    internal static IServiceCollection AddCollectionVerbs(this IServiceCollection collection) =>
        collection
            .AddVerb(() => InstallCollection)
            .AddVerb(() => RepairCuratedPluginState)
            .AddVerb(() => SeedCuratedLoadOrder)
            .AddVerb(() => SetGroupEnabled)
            .AddVerb(() => LibraryExtractFile)
            .AddVerb(() => DeleteGroup);

    [Verb("install-collection", "Installs a collection into the given loadout")]
    private static async Task<int> InstallCollection([Injected] IRenderer renderer,
        [Option("l", "loadout", "Loadout to install the collection into")] Loadout.ReadOnly loadout,
        [Option("s", "slug", "Collection slug")] string slug,
        [Option("r", "revision", "Collection revision")] int revision,
        [Injected] TemporaryFileManager temporaryFileManager,
        [Injected] ILibraryService libraryService,
        [Injected] NexusModsLibrary nexusModsLibrary,
        [Injected] IServiceProvider serviceProvider,
        [Injected] ILoginManager loginManager,
        [Injected] IConnection connection,
        [Injected] CollectionDownloader collectionDownloader,
        [Injected] CancellationToken token)
    {
        await using var destination = temporaryFileManager.CreateFile();
        var downloadJob = nexusModsLibrary.CreateCollectionDownloadJob(destination, CollectionSlug.From(slug), RevisionNumber.From((ulong)revision), token);
        
        var libraryFile = await libraryService.AddDownload(downloadJob);

        if (!libraryFile.TryGetAsNexusModsCollectionLibraryFile(out var collectionFile))
            throw new InvalidOperationException("The library file is not a NexusModsCollectionLibraryFile");

        var revisionMetadata = await nexusModsLibrary.GetOrAddCollectionRevision(collectionFile, CollectionSlug.From(slug), RevisionNumber.From((ulong)revision), token);

        if (loginManager.IsPremium)
        {
            await collectionDownloader.DownloadItems(revisionMetadata, itemType: CollectionDownloader.ItemType.Required, db: connection.Db,
                cancellationToken: token
            );
        }
        else
        {
            var tuples = collectionDownloader.GetMissingDownloadLinks(revisionMetadata, db: connection.Db, itemType: CollectionDownloader.ItemType.Required);
            if (tuples.Count > 0)
            {
                await renderer.TextLine($"Missing {tuples.Count} downloads:");
                await renderer.Table(["Name", "Uri"], tuples.Select(t => new object[] { t.Download.Name, t.Uri.ToString() }));
                return 0;
            }
        }

        var items = CollectionDownloader.GetItems(revisionMetadata, CollectionDownloader.ItemType.Required);
        await InstallCollectionJob.Create(serviceProvider, loadout, collectionFile, revisionMetadata, items);
        return 0;
    }

    [Verb("collection-repair-plugin-state", "Applies each installed collection's curated plugin enable/disable state to an existing loadout")]
    private static async Task<int> RepairCuratedPluginState([Injected] IRenderer renderer,
        [Option("l", "loadout", "Loadout to repair")] Loadout.ReadOnly loadout,
        [Injected] NexusModsLibrary nexusModsLibrary,
        [Injected] IConnection connection,
        [Injected] CancellationToken token)
    {
        var db = connection.Db;

        // A collection's manifest lists the plugins the curator actually runs -- Vortex records
        // the enabled set, not the disabled one. Anything the install produced beyond that list
        // (typically FOMOD options resolved differently than the curator's setup) is a plugin the
        // curator does not load, and each such plugin can be a missing master that crashes the
        // game on startup. The manifest is read straight out of the collection archive in the
        // library, so this works fully offline.
        foreach (var collectionGroup in NexusCollectionLoadoutGroup.All(db))
        {
            var groupItem = collectionGroup.AsCollectionGroup().AsLoadoutItemGroup().AsLoadoutItem();
            if (groupItem.LoadoutId != loadout.LoadoutId) continue;

            var root = await nexusModsLibrary.ParseCollectionJsonFile(collectionGroup.LibraryFile, token);

            var curatorPlugins = CollectionPluginWhitelist.CuratorEnabledPlugins(root);
            if (curatorPlugins.Count == 0)
            {
                await renderer.TextLine($"Collection `{groupItem.Name}`: manifest lists no plugins; nothing to do");
                continue;
            }

            using var tx = connection.BeginTransaction();
            var disabled = CollectionPluginWhitelist.DisableExtraPlugins(db, tx, curatorPlugins, loadout.LoadoutId, collectionGroup.Id);

            if (disabled > 0) await tx.Commit();
            await renderer.TextLine($"Collection `{groupItem.Name}`: curator runs {curatorPlugins.Count} plugin(s); disabled {disabled} the install added beyond that");
        }

        await renderer.TextLine("Re-apply the loadout to update the game folder");
        return 0;
    }

    [Verb("collection-seed-load-order", "Applies each installed collection's curated load order to an existing loadout")]
    private static async Task<int> SeedCuratedLoadOrder([Injected] IRenderer renderer,
        [Option("l", "loadout", "Loadout to seed")] Loadout.ReadOnly loadout,
        [Injected] NexusModsLibrary nexusModsLibrary,
        [Injected] IConnection connection,
        [Injected] IServiceProvider serviceProvider,
        [Injected] CancellationToken token)
    {
        // The curated order is read straight out of the collection archive in the library, so this
        // works fully offline — the path for installs that predate load-order seeding.
        var db = connection.Db;
        var gameId = loadout.InstallationInstance.Game.GameId;
        var seeders = serviceProvider.GetServices<ICollectionLoadOrderSeeder>()
            .Where(seeder => seeder.GameIds.Contains(gameId))
            .ToArray();

        if (seeders.Length == 0)
        {
            await renderer.TextLine("This game has no curated load-order support");
            return 0;
        }

        var seeded = 0;
        foreach (var collectionGroup in NexusCollectionLoadoutGroup.All(db))
        {
            var groupItem = collectionGroup.AsCollectionGroup().AsLoadoutItemGroup().AsLoadoutItem();
            if (groupItem.LoadoutId != loadout.LoadoutId) continue;

            var root = await nexusModsLibrary.ParseCollectionJsonFile(collectionGroup.LibraryFile, token);
            foreach (var seeder in seeders)
            {
                await seeder.SeedAsync(root, loadout.LoadoutId, collectionGroup.AsCollectionGroup().CollectionGroupId, token);
            }
            seeded++;
            await renderer.TextLine($"Collection `{groupItem.Name}`: curated load order applied");
        }

        if (seeded == 0)
        {
            await renderer.TextLine("No installed collections found in this loadout");
        }
        else
        {
            await renderer.TextLine("Re-apply the loadout to write the new plugins.txt");
        }
        return 0;
    }

    [Verb("library-extract-file", "Extracts files matching a name from library archives to a folder on disk")]
    private static async Task<int> LibraryExtractFile([Injected] IRenderer renderer,
        [Option("n", "name", "File name to match (case-insensitive, exact file name)")] string fileName,
        [Option("o", "output", "Output folder")] AbsolutePath output,
        [Option("p", "parent", "Only match entries whose parent archive name contains this (case-insensitive)", true)] string? parentFilter,
        [Injected] IConnection connection,
        [Injected] Apocrypha.Sdk.FileStore.IFileStore fileStore,
        [Injected] CancellationToken token)
    {
        var db = connection.Db;
        var matches = LibraryArchiveFileEntry.All(db)
            .Where(entry => string.Equals(entry.Path.FileName.ToString(), fileName, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrEmpty(parentFilter)
                            || entry.Parent.AsLibraryFile().FileName.ToString().Contains(parentFilter, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(entry => entry.AsLibraryFile().Hash)
            // Safety cap: this is a debugging verb; matching more than 20 distinct archives
            // almost always means the name filter is too broad.
            .Take(20)
            .ToArray();

        if (matches.Length == 0)
        {
            await renderer.TextLine($"No library archive entry named `{fileName}`");
            return 1;
        }

        output.CreateDirectory();
        foreach (var entry in matches)
        {
            var parentName = entry.Parent.AsLibraryFile().FileName;
            var target = output.Combine(RelativePath.FromUnsanitizedInput($"{parentName}--{entry.Path.FileName}"));
            await using var src = await fileStore.GetFileStream(entry.AsLibraryFile().Hash, token);
            await using var dst = target.Create();
            await src.CopyToAsync(dst, token);
            await renderer.TextLine($"{entry.Parent.AsLibraryFile().FileName} :: {entry.Path} -> {target}");
        }
        return 0;
    }

    [Verb("loadout-group-delete", "Deletes a loadout group by exact name, including groups inside read-only collections")]
    private static async Task<int> DeleteGroup([Injected] IRenderer renderer,
        [Option("l", "loadout", "Loadout containing the group")] Loadout.ReadOnly loadout,
        [Option("g", "group", "Exact name of the group to delete")] string groupName,
        [Injected] ILoadoutManager loadoutManager,
        [Injected] IConnection connection,
        [Injected] CancellationToken token)
    {
        var db = connection.Db;
        var matches = LoadoutItem.FindByLoadout(db, loadout)
            .Where(item => item.IsLoadoutItemGroup() && string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            await renderer.TextLine($"No group named `{groupName}` in loadout `{loadout.Name}`");
            return 1;
        }
        if (matches.Length > 1)
        {
            await renderer.TextLine($"`{groupName}` matches {matches.Length} groups; refusing to guess");
            return 1;
        }

        if (!matches[0].TryGetAsLoadoutItemGroup(out var group))
        {
            await renderer.TextLine($"`{groupName}` is not a group");
            return 1;
        }

        var childCount = group.Children.Count();
        await loadoutManager.RemoveItems([group.LoadoutItemGroupId]);
        await renderer.TextLine($"Deleted group `{groupName}` ({childCount} items). A collection retry can now reinstall it.");
        return 0;
    }

    [Verb("loadout-group-set-enabled", "Enables or disables a loadout group by name, including groups the UI treats as read-only")]
    private static async Task<int> SetGroupEnabled([Injected] IRenderer renderer,
        [Option("l", "loadout", "Loadout containing the group")] Loadout.ReadOnly loadout,
        [Option("g", "group", "Exact name of the group")] string groupName,
        [Option("e", "enabled", "true to enable, false to disable")] bool enabled,
        [Injected] IConnection connection,
        [Injected] CancellationToken token)
    {
        var db = connection.Db;
        var matches = LoadoutItem.FindByLoadout(db, loadout)
            .Where(item => item.IsLoadoutItemGroup() && string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            await renderer.TextLine($"No group named `{groupName}` in loadout `{loadout.Name}`");
            return 1;
        }
        if (matches.Length > 1)
        {
            await renderer.TextLine($"`{groupName}` matches {matches.Length} groups; refusing to guess");
            return 1;
        }

        var group = matches[0];
        using var tx = connection.BeginTransaction();
        if (enabled) tx.Retract(group.Id, LoadoutItem.Disabled, Null.Instance);
        else tx.Add(group.Id, LoadoutItem.Disabled, Null.Instance);
        await tx.Commit();

        await renderer.TextLine($"Group `{group.Name}` is now {(enabled ? "enabled" : "DISABLED")}; re-apply the loadout to update the game folder");
        return 0;
    }
}
