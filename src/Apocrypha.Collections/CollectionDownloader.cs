using System.Diagnostics;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.Sdk.Settings;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.DatomIterators;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using NexusMods.MnemonicDB.Abstractions.IndexSegments;
using NexusMods.MnemonicDB.Abstractions.Query;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using Apocrypha.Networking.NexusWebApi;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.NexusModsApi;
using OneOf;
using Reloaded.Memory.Extensions;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Collections;

/// <summary>
/// Methods for collection downloads.
/// </summary>
[PublicAPI]
public class CollectionDownloader
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IConnection _connection;
    private readonly ILoginManager _loginManager;
    private readonly TemporaryFileManager _temporaryFileManager;
    private readonly NexusModsLibrary _nexusModsLibrary;
    private readonly ILibraryService _libraryService;
    private readonly IOSInterop _osInterop;
    private readonly HttpClient _httpClient;
    private readonly IJobMonitor _jobMonitor;
    private readonly IGameDomainToGameIdMappingCache _mappingCache;

    /// <summary>
    /// Constructor.
    /// </summary>
    public CollectionDownloader(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<CollectionDownloader>>();
        _connection = serviceProvider.GetRequiredService<IConnection>();
        _loginManager = serviceProvider.GetRequiredService<ILoginManager>();
        _temporaryFileManager = serviceProvider.GetRequiredService<TemporaryFileManager>();
        _nexusModsLibrary = serviceProvider.GetRequiredService<NexusModsLibrary>();
        _libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        _osInterop = serviceProvider.GetRequiredService<IOSInterop>();
        _httpClient = serviceProvider.GetRequiredService<HttpClient>();
        _jobMonitor = serviceProvider.GetRequiredService<IJobMonitor>();
        _mappingCache = serviceProvider.GetRequiredService<IGameDomainToGameIdMappingCache>();
    }

    /// <summary>
    /// Gets or adds a revision.
    /// </summary>
    public async ValueTask<CollectionRevisionMetadata.ReadOnly> GetOrAddRevision(CollectionSlug slug, RevisionNumber revisionNumber, CancellationToken cancellationToken)
    {
        var revisions = CollectionRevisionMetadata
            .FindByRevisionNumber(_connection.Db, revisionNumber)
            .Where(r => r.Collection.Slug == slug);

        if (revisions.TryGetFirst(out var revision)) return revision;

        await using var destination = _temporaryFileManager.CreateFile();
        var downloadJob = _nexusModsLibrary.CreateCollectionDownloadJob(destination, slug, revisionNumber, CancellationToken.None);

        var libraryFile = await _libraryService.AddDownload(downloadJob);

        if (!libraryFile.TryGetAsNexusModsCollectionLibraryFile(out var collectionFile))
            throw new InvalidOperationException("The library file is not a NexusModsCollectionLibraryFile");

        revision = await _nexusModsLibrary.GetOrAddCollectionRevision(collectionFile, slug, revisionNumber, cancellationToken);
        return revision;
    }

    record DirectDownloadResult(bool CanDownload, Optional<RelativePath> FileName = default)
    {
        public static readonly DirectDownloadResult Unable = new(CanDownload: false);
    };

    private async ValueTask<DirectDownloadResult> CanDirectDownload(
        CollectionDownloadExternal.ReadOnly download,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Testing if `{Uri}` can be downloaded directly", download.Uri);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, download.Uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode) return DirectDownloadResult.Unable;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("application/"))
            {
                _logger.LogInformation("Download at `{Uri}` can't be downloaded automatically because Content-Type `{ContentType}` doesn't indicate a binary download", download.Uri, contentType);
                return DirectDownloadResult.Unable;
            }

            if (!response.Content.Headers.ContentLength.HasValue)
            {
                _logger.LogInformation("Download at `{Uri}` can't be downloaded automatically because the response doesn't have a Content-Length", download.Uri);
                return DirectDownloadResult.Unable;
            }

            var size = Size.FromLong(response.Content.Headers.ContentLength.Value);
            if (size != download.Size)
            {
                _logger.LogWarning("Download at `{Uri}` can't be downloaded automatically because the Content-Length `{ContentLength}` doesn't match the expected size `{ExpectedSize}`", download.Uri, size, download.Size);
                return DirectDownloadResult.Unable;
            }

            var contentDispositionFileName = response.Content.Headers.ContentDisposition?.FileName;
            var fileName = contentDispositionFileName is null ? Optional<RelativePath>.None : RelativePath.FromUnsanitizedInput(contentDispositionFileName);

            return new DirectDownloadResult(CanDownload: true, FileName: fileName);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception while checking if `{Uri}` can be downloaded directly", download.Uri);
            return DirectDownloadResult.Unable;
        }
    }

    /// <summary>
    /// Downloads an external file.
    /// </summary>
    public async ValueTask Download(CollectionDownloadExternal.ReadOnly download, CancellationToken cancellationToken)
    {
        var result = await CanDirectDownload(download, cancellationToken);
        if (result.CanDownload)
        {
            _logger.LogInformation("Downloading external file directly at `{Uri}` (`{Hash}`)", download.Uri, download.Md5);

            if (download.Rebase(_connection.Db).IsManualOnly)
            {
                using var tx = _connection.BeginTransaction();
                tx.Retract(download.Id, CollectionDownloadExternal.ManualOnly, Null.Instance);
                await tx.Commit();
            }

            var job = ExternalDownloadJob.Create(
                _serviceProvider,
                download.Uri,
                download.Md5,
                logicalFileName: download.AsCollectionDownload().Name,
                fileName: result.FileName
            );

            await _libraryService.AddDownload(job);
        }
        else
        {
            _logger.LogInformation("Unable to direct download `{Uri}` (`{Hash}`)", download.Uri, download.Md5);
            if (download.Rebase(_connection.Db).IsManualOnly) return;

            using var tx = _connection.BeginTransaction();
            tx.Add(download.Id, CollectionDownloadExternal.ManualOnly, Null.Instance);
            await tx.Commit();
        }
    }

    /// <summary>
    /// Downloads a file from nexus mods for premium users or opens the download page in the browser.
    /// </summary>
    public async ValueTask Download(CollectionDownloadNexusMods.ReadOnly download, CancellationToken cancellationToken)
    {
        var userInfo = await _loginManager.GetUserInfoAsync(cancellationToken);
        if (userInfo is null) return;

        if (userInfo.UserRole is UserRole.Premium)
        {
            await using var tempPath = _temporaryFileManager.CreateFile();
            var job = await _nexusModsLibrary.CreateDownloadJob(tempPath, download.FileMetadata, parentRevision: download.AsCollectionDownload().CollectionRevision, cancellationToken: cancellationToken);
            await _libraryService.AddDownload(job);
        }
        else
        {
            var domain = _mappingCache[download.FileUid.GameId];
            _osInterop.OpenUri(NexusModsUrlBuilder.GetFileDownloadUri(domain, download.ModUid.ModId, download.FileUid.FileId, useNxmLink: true));
        }
    }

    /// <summary>
    /// Returns an observable with the number of downloaded items.
    /// </summary>
    public IObservable<int> DownloadedItemCountObservable(CollectionRevisionMetadata.ReadOnly revisionMetadata, ItemType itemType)
    {
        return _connection
            .ObserveDatoms(CollectionDownload.CollectionRevision, revisionMetadata)
            .AsEntityIds()
            .Transform(datom => CollectionDownload.Load(_connection.Db, datom.E))
            .FilterImmutable(download => DownloadMatchesItemType(download, itemType))
            .TransformOnObservable(download => GetStatusObservable(download, Observable.Return(Optional<CollectionGroup.ReadOnly>.None)))
            .FilterImmutable(static status => status.IsDownloaded() && !status.IsBundled())
            .QueryWhenChanged(query => query.Count)
            .Prepend(0);
    }

    /// <summary>
    /// Counts the items.
    /// </summary>
    public static int CountItems(CollectionRevisionMetadata.ReadOnly revisionMetadata, ItemType itemType)
    {
        return revisionMetadata.Downloads
            .Where(download => DownloadMatchesItemType(download, itemType))
            .Count(download => download.IsCollectionDownloadNexusMods() || download.IsCollectionDownloadExternal());
    }

    /// <summary>
    /// Returns whether the item matches the given item type.
    /// </summary>
    internal static bool DownloadMatchesItemType(CollectionDownload.ReadOnly download, ItemType itemType)
    {
        if (download.IsOptional && itemType.HasFlagFast(ItemType.Optional)) return true;
        if (download.IsRequired && itemType.HasFlagFast(ItemType.Required)) return true;
        return false;
    }

    /// <summary>
    /// Checks whether the items in the collection were downloaded.
    /// </summary>
    public static bool IsFullyDownloaded(CollectionDownload.ReadOnly[] items, IDb db)
    {
        return items.All(download => GetStatus(download, db).IsDownloaded());
    }
    
    public static bool IsFullyInstalled(CollectionDownload.ReadOnly[] items, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db)
    {
        return items.All(download => GetStatus(download, collectionGroup, db).IsInstalled(out _));
    }

    [Flags, PublicAPI]
    public enum ItemType
    {
        Required = 1,
        Optional = 2,
    };

    /// <summary>
    /// Downloads everything in the revision.
    /// </summary>
    public async ValueTask DownloadItems(
        CollectionRevisionMetadata.ReadOnly revisionMetadata,
        ItemType itemType,
        IDb db,
        CancellationToken cancellationToken = default)
    {
        var job = new DownloadCollectionJob
        {
            Downloader = this,
            Logger = _serviceProvider.GetRequiredService<ILogger<DownloadCollectionJob>>(),
            RevisionMetadata = revisionMetadata,
            Db = db,
            ItemType = itemType,
            MaxDegreeOfParallelism = _serviceProvider.GetRequiredService<ISettingsManager>().Get<DownloadSettings>().MaxParallelDownloads,
        };

        await _jobMonitor.Begin<DownloadCollectionJob, R3.Unit>(job);
    }

    /// <summary>
    /// Checks whether the collection is installed.
    /// </summary>
    public IObservable<bool> IsCollectionInstalledObservable(
        CollectionRevisionMetadata.ReadOnly revision, 
        IObservable<Optional<CollectionGroup.ReadOnly>> groupObservable, 
        ItemType itemType = ItemType.Required)
    {
        var observables = revision.Downloads
            .Where(download => DownloadMatchesItemType(download, itemType))
            .Select(download => GetStatusObservable(download, groupObservable).Select(static status => status.IsInstalled(out _)))
            .ToArray();

        if (observables.Length == 0) return groupObservable.Select(static optional => optional.HasValue);
        return observables.CombineLatest(static list => list.All(static installed => installed));
    }

    /// <summary>
    /// Returns all missing downloads and Uris.
    /// </summary>
    public IReadOnlyList<(CollectionDownload.ReadOnly Download, Uri Uri)> GetMissingDownloadLinks(CollectionRevisionMetadata.ReadOnly revision, IDb db, ItemType itemType = ItemType.Required)
    {
        var results = new List<(CollectionDownload.ReadOnly Download, Uri Uri)>();
        var downloads = GetItems(revision, itemType).Where(download => GetStatus(download, db).IsNotDownloaded());

        foreach (var download in downloads)
        {
            if (download.TryGetAsCollectionDownloadNexusMods(out var nexusModsDownload))
            {
                var domain = _mappingCache[nexusModsDownload.FileUid.GameId];
                var uri = NexusModsUrlBuilder.GetFileDownloadUri(domain, nexusModsDownload.ModUid.ModId, nexusModsDownload.FileUid.FileId, useNxmLink: false);
                results.Add((download, uri));
            } else if (download.TryGetAsCollectionDownloadExternal(out var externalDownload))
            {
                results.Add((download, externalDownload.Uri));
            }
        }

        return results;
    }

    private static CollectionDownloadStatus GetStatus(CollectionDownloadBundled.ReadOnly download, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db)
    {
        if (!collectionGroup.HasValue) return new CollectionDownloadStatus.Bundled();

        var entityIds = db.Datoms(
            (NexusCollectionBundledLoadoutGroup.BundleDownload, download),
            (LoadoutItem.ParentId, collectionGroup.Value)
        );

        foreach (var entityId in entityIds)
        {
            var loadoutItem = LoadoutItem.Load(db, entityId);
            if (loadoutItem.IsValid()) return new CollectionDownloadStatus.Installed(loadoutItem);
        }

        return new CollectionDownloadStatus.Bundled();
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownloadBundled.ReadOnly download,
        IObservable<Optional<CollectionGroup.ReadOnly>> groupObservable)
    {
        return _connection
            .ObserveDatoms(NexusCollectionBundledLoadoutGroup.BundleDownload, download)
            .TransformImmutable(datom => LoadoutItem.Load(_connection.Db, datom.E))
            .FilterOnObservable(item =>
            {
                return groupObservable
                    .Select(optional => optional.Convert(static group => group.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId))
                    .Select(loadoutId => loadoutId.HasValue && item.LoadoutId == loadoutId.Value);
            })
            .QueryWhenChanged(query => query.Items.FirstOrOptional(static _ => true))
            .Select(optional =>
            {
                if (!optional.HasValue) return (CollectionDownloadStatus) new CollectionDownloadStatus.Bundled();
                return new CollectionDownloadStatus.Installed(optional.Value);
            })
            .Prepend(new CollectionDownloadStatus.Bundled());
    }

    private static CollectionDownloadStatus GetStatus(CollectionDownloadNexusMods.ReadOnly download, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db, bool requireCollectionItemTag)
    {
        var datoms = db.Datoms(NexusModsLibraryItem.FileMetadata, download.FileMetadata);
        if (datoms.Count == 0) return new CollectionDownloadStatus.NotDownloaded();

        var libraryItem = default(NexusModsLibraryItem.ReadOnly);
        foreach (var datom in datoms)
        {
            libraryItem = NexusModsLibraryItem.Load(db, datom.E);
            if (libraryItem.IsValid()) break;
        }

        if (!libraryItem.IsValid()) return new CollectionDownloadStatus.NotDownloaded();
        return GetStatus(libraryItem.AsLibraryItem(), collectionGroup, db, requireCollectionItemTag);
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownloadNexusMods.ReadOnly download,
        IObservable<Optional<CollectionGroup.ReadOnly>> groupObservable)
    {
        return _connection
            .ObserveDatoms(NexusModsLibraryItem.FileMetadata, download.FileMetadata)
            .QueryWhenChanged(query => query.Items.FirstOrOptional(static _ => true))
            .DistinctUntilChanged(OptionalDatomComparer.Instance)
            .SelectMany(optional =>
            {
                if (!optional.HasValue) return Observable.Return<CollectionDownloadStatus>(new CollectionDownloadStatus.NotDownloaded());

                var libraryItem = LibraryItem.Load(_connection.Db, optional.Value.E);
                Debug.Assert(libraryItem.IsValid());

                return GetStatusObservable(libraryItem, groupObservable);
            });
    }

    private static CollectionDownloadStatus GetStatus(CollectionDownloadExternal.ReadOnly download, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db, bool requireCollectionItemTag)
    {
        var datoms = db.Datoms(LibraryFile.Md5, download.Md5);
        if (datoms.Count == 0) return new CollectionDownloadStatus.NotDownloaded();

        foreach (var datom in datoms)
        {
            var libraryFile = DirectDownloadLibraryFile.Load(db, datom.E).AsLocalFile().AsLibraryFile();
            if (libraryFile.IsValid()) return GetStatus(libraryFile.AsLibraryItem(), collectionGroup, db, requireCollectionItemTag);
        }

        return new CollectionDownloadStatus.NotDownloaded();
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownloadExternal.ReadOnly download,
        IObservable<Optional<CollectionGroup.ReadOnly>> groupObservable)
    {
        var observable = _connection.ObserveDatoms(SliceDescriptor.Create(LibraryFile.Md5, download.Md5, _connection.AttributeCache));

        return observable
            .QueryWhenChanged(query => query.Items.FirstOrOptional(static _ => true))
            .Prepend(Optional<Datom>.None)
            .DistinctUntilChanged(OptionalDatomComparer.Instance)
            .SelectMany(optional =>
            {
                if (!optional.HasValue) return Observable.Return<CollectionDownloadStatus>(new CollectionDownloadStatus.NotDownloaded());

                var libraryItem = LibraryItem.Load(_connection.Db, optional.Value.E);
                Debug.Assert(libraryItem.IsValid());

                return GetStatusObservable(libraryItem, groupObservable);
            });
    }

    private static CollectionDownloadStatus GetStatus(
        LibraryItem.ReadOnly libraryItem,
        Optional<CollectionGroup.ReadOnly> collectionGroup,
        IDb db,
        bool requireCollectionItemTag)
    {
        if (!collectionGroup.HasValue) return new CollectionDownloadStatus.InLibrary(libraryItem);

        var entityIds = db.Datoms(
            (LibraryLinkedLoadoutItem.LibraryItem, libraryItem),
            (LoadoutItem.ParentId, collectionGroup.Value)
        );

        if (entityIds.Count == 0) return new CollectionDownloadStatus.InLibrary(libraryItem);

        foreach (var entityId in entityIds)
        {
            var loadoutItem = LoadoutItem.Load(db, entityId);
            if (!loadoutItem.IsValid()) continue;
            if (requireCollectionItemTag && !HasCollectionItemTag(loadoutItem)) continue;
            return new CollectionDownloadStatus.Installed(loadoutItem);
        }

        return new CollectionDownloadStatus.InLibrary(libraryItem);
    }

    /// <summary>
    /// Whether a loadout item under a collection group was ever claimed by the collection installer
    /// (review finding S5-1, detection half).
    ///
    /// <para>
    /// The standard installer chain commits the group first and only tags it as a
    /// <see cref="NexusCollectionItemLoadoutGroup"/> in a follow-up transaction, so a crash in between
    /// leaves a group that is installed and deployed but was never claimed. Treating that as installed
    /// is what made the state permanent: the install job skips anything reporting installed, so no
    /// retry could heal it.
    /// </para>
    ///
    /// <para>
    /// Presence of <em>either</em> tag attribute counts, deliberately. Migration
    /// <c>_0002_NexusCollectionItem</c> backfills <see cref="NexusCollectionItemLoadoutGroup.IsRequired"/>
    /// alone for pre-tag items it cannot match to a download ("set a default value and hope for the
    /// best"), so a genuine legacy install can carry <c>IsRequired</c> without
    /// <see cref="NexusCollectionItemLoadoutGroup.Download"/>. Requiring <c>Download</c> would report
    /// every one of those users' installed mods as merely in-library. A group stranded by the crash
    /// window above carries neither attribute, so "either" separates the two cases exactly.
    /// </para>
    /// </summary>
    private static bool HasCollectionItemTag(LoadoutItem.ReadOnly loadoutItem)
    {
        return NexusCollectionItemLoadoutGroup.Download.IsIn(loadoutItem) ||
               NexusCollectionItemLoadoutGroup.IsRequired.IsIn(loadoutItem);
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        LibraryItem.ReadOnly libraryItem,
        IObservable<Optional<CollectionGroup.ReadOnly>> groupObservable)
    {
        return _connection
            .ObserveDatoms(LibraryLinkedLoadoutItem.LibraryItemId, libraryItem.LibraryItemId)
            .TransformImmutable(datom => LibraryLinkedLoadoutItem.Load(_connection.Db, datom.E))
            .FilterOnObservable(item =>
            {
                return groupObservable
                    .Select(group =>
                    {
                        if (!group.HasValue) return false;
                        // A standalone (non-collection) install of the same library item has no
                        // Parent at all — it just isn't a match for this group, not an error.
                        if (!LoadoutItem.ParentId.TryGetValue(item, out var parentId)) return false;
                        var itemLoadoutId = LoadoutItem.LoadoutId.Get(item);
                        var groupLoadoutId = LoadoutItem.LoadoutId.Get(group.Value);
                        var id = group.Value.Id;

                        return itemLoadoutId == groupLoadoutId && parentId == id;
                    });
            })
            .QueryWhenChanged(query =>
            {
                var optional = query.Items.FirstOrOptional(static x => true);

                CollectionDownloadStatus status = optional.HasValue
                    ? new CollectionDownloadStatus.Installed(optional.Value.AsLoadoutItemGroup().AsLoadoutItem())
                    : new CollectionDownloadStatus.InLibrary(libraryItem);

                return status;
            })
            .Prepend(new CollectionDownloadStatus.InLibrary(libraryItem));
    }

    /// <summary>
    /// Gets the status of a download as an observable.
    /// </summary>
    public IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownload.ReadOnly download,
        IObservable<Optional<CollectionGroup.ReadOnly>> groupObservable)
    {
        if (download.TryGetAsCollectionDownloadBundled(out var bundled))
        {
            return GetStatusObservable(bundled, groupObservable).DistinctUntilChanged();
        }

        if (download.TryGetAsCollectionDownloadNexusMods(out var nexusModsDownload))
        {
            return GetStatusObservable(nexusModsDownload, groupObservable).DistinctUntilChanged();
        }

        if (download.TryGetAsCollectionDownloadExternal(out var externalDownload))
        {
            return GetStatusObservable(externalDownload, groupObservable).DistinctUntilChanged();
        }

        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the status of a download.
    /// </summary>
    public static CollectionDownloadStatus GetStatus(CollectionDownload.ReadOnly download, IDb db)
    {
        return GetStatus(download, new Optional<CollectionGroup.ReadOnly>(), db);
    }

    /// <summary>
    /// Gets the status of a download.
    /// </summary>
    public static CollectionDownloadStatus GetStatus(
        CollectionDownload.ReadOnly download,
        Optional<CollectionGroup.ReadOnly> collectionGroup,
        IDb db)
    {
        return GetStatus(download, collectionGroup, db, requireCollectionItemTag: true);
    }

    /// <summary>
    /// Gets the status of a download, counting an untagged loadout item under the collection group as
    /// installed.
    ///
    /// <para>
    /// Exists for migration <c>_0002_NexusCollectionItem</c> only, which calls status to decide which
    /// pre-tag loadout items to tag. Asking the tag-aware question there would find nothing installed,
    /// tag nothing, and silently orphan every existing user's collection items on upgrade — the exact
    /// reason the detection half of S5-1 could not be fixed in isolation. Nothing else should use this:
    /// see <see cref="HasCollectionItemTag"/> for why an untagged group is a failed install, not a
    /// healthy one.
    /// </para>
    /// </summary>
    internal static CollectionDownloadStatus GetStatusIgnoringCollectionItemTag(
        CollectionDownload.ReadOnly download,
        Optional<CollectionGroup.ReadOnly> collectionGroup,
        IDb db)
    {
        return GetStatus(download, collectionGroup, db, requireCollectionItemTag: false);
    }

    private static CollectionDownloadStatus GetStatus(
        CollectionDownload.ReadOnly download,
        Optional<CollectionGroup.ReadOnly> collectionGroup,
        IDb db,
        bool requireCollectionItemTag)
    {
        if (download.TryGetAsCollectionDownloadBundled(out var bundled))
        {
            // No tag check: a bundled group mints its BundleDownload reference and its
            // NexusCollectionItemLoadoutGroup tag in the same transaction as the install, so it can
            // never be found in the untagged state the check exists to catch.
            return GetStatus(bundled, collectionGroup, db);
        }

        if (download.TryGetAsCollectionDownloadNexusMods(out var nexusModsDownload))
        {
            return GetStatus(nexusModsDownload, collectionGroup, db, requireCollectionItemTag);
        }

        if (download.TryGetAsCollectionDownloadExternal(out var externalDownload))
        {
            return GetStatus(externalDownload, collectionGroup, db, requireCollectionItemTag);
        }

        throw new NotSupportedException();
    }

    /// <summary>
    /// Deletes all associated collection loadout groups.
    /// </summary>
    public async ValueTask DeleteCollectionLoadoutGroup(CollectionRevisionMetadata.ReadOnly revision, CancellationToken cancellationToken)
    {
        var db = _connection.Db;
        using var tx = _connection.BeginTransaction();

        var groupDatoms = db.Datoms(NexusCollectionLoadoutGroup.Revision, revision);
        foreach (var datom in groupDatoms)
        {
            tx.Delete(datom.E, recursive: true);
        }

        await tx.Commit();
    }

    /// <summary>
    /// Compensating action for a collection item install that failed after its loadout group was
    /// already committed (review finding S5-1).
    ///
    /// <para>
    /// The standard-chain and FOMOD install branches have to commit the group before curator patches
    /// can be resolved, because patch keys are relative to the *installed* layout and that layout is
    /// only queryable once the group exists. If patching or the follow-up tagging transaction then
    /// fails, the group is left installed and deployed but unpatched — and
    /// <see cref="GetStatus(CollectionDownload.ReadOnly, Optional{CollectionGroup.ReadOnly}, IDb)"/>
    /// reports it as installed, so the install job skips it forever and no retry heals it.
    /// </para>
    ///
    /// <para>
    /// Removing the group returns the download to "in library", which is a state a retry *can* heal.
    /// Deliberately recursive: the group's files must go with it, or the next attempt collides with
    /// the half-installed remains.
    /// </para>
    /// </summary>
    public static async ValueTask RetractStrandedItemGroup(IConnection connection, EntityId groupId)
    {
        using var tx = connection.BeginTransaction();
        tx.Delete(groupId, recursive: true);
        await tx.Commit();
    }

    /// <summary>
    /// Sweeps up groups stranded by a crash rather than by a caught exception (review finding S5-1,
    /// detection half), returning the ids that were removed.
    ///
    /// <para>
    /// <see cref="RetractStrandedItemGroup"/> only runs when the install job catches the failure. Kill
    /// the process between the group's commit and its tagging transaction and the compensating retract
    /// never happens, leaving an installed, deployed, unclaimed group behind. Status now reports that
    /// group as in-library so a retry no longer skips it — but a retry would install a second group
    /// alongside the remains unless they are cleared first, which is what this does.
    /// </para>
    ///
    /// <para>
    /// Deliberately narrow: only groups parented to <paramref name="collectionGroup"/>, linked to a
    /// library item one of <paramref name="downloads"/> resolves to, and carrying no
    /// <see cref="NexusCollectionItemLoadoutGroup"/> attribute at all are touched. That is the exact
    /// shape of the crash window; a legacy item half-tagged by migration <c>_0002_NexusCollectionItem</c>
    /// keeps its <c>IsRequired</c> and is therefore never swept (see <see cref="HasCollectionItemTag"/>).
    /// </para>
    /// </summary>
    public static async ValueTask<EntityId[]> RetractStrandedItemGroups(
        IConnection connection,
        IEnumerable<CollectionDownload.ReadOnly> downloads,
        CollectionGroup.ReadOnly collectionGroup)
    {
        var db = connection.Db;
        Optional<CollectionGroup.ReadOnly> group = collectionGroup;

        var stranded = new HashSet<EntityId>();
        foreach (var download in downloads)
        {
            if (!GetStatusIgnoringCollectionItemTag(download, group, db).IsInstalled(out var loadoutItem)) continue;
            if (HasCollectionItemTag(loadoutItem)) continue;
            stranded.Add(loadoutItem.Id);
        }

        if (stranded.Count == 0) return [];

        using var tx = connection.BeginTransaction();
        foreach (var id in stranded)
        {
            tx.Delete(id, recursive: true);
        }

        await tx.Commit();
        return stranded.ToArray();
    }

    /// <summary>
    /// Returns all items of the desired type (required/optional).
    /// </summary>
    public static CollectionDownload.ReadOnly[] GetItems(CollectionRevisionMetadata.ReadOnly revision, ItemType itemType)
    {
        var res = new CollectionDownload.ReadOnly[revision.Downloads.Count];

        var i = 0;
        foreach (var download in revision.Downloads)
        {
            if (!DownloadMatchesItemType(download, itemType)) continue;
            res[i++] = download;
        }

        Array.Resize(ref res, newSize: i);
        return res;
    }

    /// <summary>
    /// Gets the library file for the collection.
    /// </summary>
    public NexusModsCollectionLibraryFile.ReadOnly GetLibraryFile(CollectionRevisionMetadata.ReadOnly revisionMetadata)
    {
        var datoms = _connection.Db.Datoms(
            (NexusModsCollectionLibraryFile.CollectionSlug, revisionMetadata.Collection.Slug),
            (NexusModsCollectionLibraryFile.CollectionRevisionNumber, revisionMetadata.RevisionNumber)
        );

        if (datoms.Count == 0) throw new Exception($"Unable to find collection file for revision `{revisionMetadata.Collection.Slug}` (`{revisionMetadata.RevisionNumber}`)");
        var source = NexusModsCollectionLibraryFile.Load(_connection.Db, datoms[0]);
        return source;
    }

    /// <summary>
    /// Returns the collection group associated with the revision or none.
    /// </summary>
    public static Optional<NexusCollectionLoadoutGroup.ReadOnly> GetCollectionGroup(
        CollectionRevisionMetadata.ReadOnly revisionMetadata,
        LoadoutId loadoutId,
        IDb db)
    {
        var entityIds = db.Datoms(
            (NexusCollectionLoadoutGroup.Revision, revisionMetadata),
            (LoadoutItem.Loadout, loadoutId)
        );

        if (entityIds.Count == 0) return Optional.None<NexusCollectionLoadoutGroup.ReadOnly>();
        foreach (var entityId in entityIds)
        {
            var group = NexusCollectionLoadoutGroup.Load(db, entityId);
            if (group.IsValid()) return group;
        }

        return new Optional<NexusCollectionLoadoutGroup.ReadOnly>();
    }

    /// <summary>
    /// Gets an observable stream containing the collection group associated with the revision.
    /// </summary>
    public IObservable<Optional<CollectionGroup.ReadOnly>> GetCollectionGroupObservable(CollectionRevisionMetadata.ReadOnly revision, LoadoutId targetLoadout)
    {
        return _connection
            .ObserveDatoms(NexusCollectionLoadoutGroup.Revision, revision)
            .QueryWhenChanged(query =>
            {
                foreach (var datom in query.Items)
                {
                    var group = CollectionGroup.Load(_connection.Db, datom.E);
                    if (!group.IsValid()) continue;
                    if (group.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId != targetLoadout) continue;
                    return Optional<CollectionGroup.ReadOnly>.Create(group);
                }

                return Optional<CollectionGroup.ReadOnly>.None;
            })
            .Prepend(GetCollectionGroup(revision, targetLoadout, _connection.Db).Convert(static x => x.AsCollectionGroup()));
    }

    /// <summary>
    /// Deletes a revision and all downloaded entities.
    /// </summary>
    public async ValueTask DeleteRevision(CollectionRevisionMetadataId revisionId)
    {
        var db = _connection.Db;
        using var tx = _connection.BeginTransaction();

        var downloadIds = db.Datoms(CollectionDownload.CollectionRevision, revisionId);
        foreach (var downloadId in downloadIds)
        {
            tx.Delete(downloadId.E, recursive: false);
        }

        tx.Delete(revisionId, recursive: false);

        await tx.Commit();
    }

    /// <summary>
    /// Deletes a collection, all revisions, and all download entities of all revisions.
    /// </summary>
    public async ValueTask DeleteCollection(CollectionMetadataId collectionMetadataId)
    {
        var db = _connection.Db;
        using var tx = _connection.BeginTransaction();

        var revisionIds = db.Datoms(CollectionRevisionMetadata.CollectionId, collectionMetadataId);
        foreach (var revisionId in revisionIds)
        {
            var downloadIds = db.Datoms(CollectionDownload.CollectionRevision, revisionId.E);
            foreach (var downloadId in downloadIds)
            {
                tx.Delete(downloadId.E, recursive: false);
            }

            tx.Delete(revisionId.E, recursive: false);
        }

        tx.Delete(collectionMetadataId, recursive: false);

        await tx.Commit();
    }

    /// <summary>
    /// Returns all collections for the given game.
    /// </summary>
    public static CollectionMetadata.ReadOnly[] GetCollections(IDb db, NexusModsGameId nexusModsGameId)
    {
        return CollectionMetadata.FindByGameId(db, nexusModsGameId).ToArray();
    }
}

/// <summary>
/// Represents the current status of a download in a collection.
/// </summary>
[PublicAPI]
[DebuggerDisplay("{Value}")]
public readonly struct CollectionDownloadStatus : IEquatable<CollectionDownloadStatus>
{
    /// <summary>
    /// Value.
    /// </summary>
    public readonly OneOf<NotDownloaded, Bundled, InLibrary, Installed> Value;

    /// <summary>
    /// Constructor.
    /// </summary>
    public CollectionDownloadStatus(OneOf<NotDownloaded, Bundled, InLibrary, Installed> value)
    {
        Value = value;
    }

    /// <summary>
    /// Item hasn't been downloaded yet.
    /// </summary>
    public readonly struct NotDownloaded;

    /// <summary>
    /// For bundled downloads.
    /// </summary>
    public readonly struct Bundled;

    /// <summary>
    /// For items that have been downloaded and added to the library.
    /// </summary>
    public readonly struct InLibrary : IEquatable<InLibrary>
    {
        /// <summary>
        /// The library item.
        /// </summary>
        public readonly LibraryItem.ReadOnly LibraryItem;

        /// <summary>
        /// Constructor.
        /// </summary>
        public InLibrary(LibraryItem.ReadOnly libraryItem)
        {
            LibraryItem = libraryItem;
        }

        /// <inheritdoc/>
        public bool Equals(InLibrary other) => LibraryItem.LibraryItemId == other.LibraryItem.LibraryItemId;
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is InLibrary other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => LibraryItem.Id.GetHashCode();
    }

    /// <summary>
    /// For items that have been installed.
    /// </summary>
    public readonly struct Installed : IEquatable<Installed>
    {
        /// <summary>
        /// The loadout item.
        /// </summary>
        public readonly LoadoutItem.ReadOnly LoadoutItem;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Installed(LoadoutItem.ReadOnly loadoutItem)
        {
            LoadoutItem = loadoutItem;
        }

        /// <inheritdoc/>
        public bool Equals(Installed other) => LoadoutItem.LoadoutItemId == other.LoadoutItem.LoadoutItemId;
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Installed other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => LoadoutItem.Id.GetHashCode();
    }

    public bool IsNotDownloaded() => Value.IsT0;
    public bool IsDownloaded() => !IsNotDownloaded();
    public bool IsBundled() => Value.IsT1;

    public bool IsInLibrary(out LibraryItem.ReadOnly libraryItem)
    {
        if (!Value.TryPickT2(out var value, out _))
        {
            libraryItem = default(LibraryItem.ReadOnly);
            return false;
        }

        libraryItem = value.LibraryItem;
        return true;
    }

    public bool IsInstalled(out LoadoutItem.ReadOnly loadoutItem)
    {
        if (!Value.TryPickT3(out var value, out _))
        {
            loadoutItem = default(LoadoutItem.ReadOnly);
            return false;
        }

        loadoutItem = value.LoadoutItem;
        return true;
    }

    public static implicit operator CollectionDownloadStatus(NotDownloaded x) => new(x);
    public static implicit operator CollectionDownloadStatus(Bundled x) => new(x);
    public static implicit operator CollectionDownloadStatus(InLibrary x) => new(x);
    public static implicit operator CollectionDownloadStatus(Installed x) => new(x);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CollectionDownloadStatus other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(CollectionDownloadStatus other)
    {
        var (index, otherIndex) = (Value.Index, other.Value.Index);
        if (index != otherIndex) return false;

        if (IsNotDownloaded()) return true;
        if (IsBundled()) return true;

        if (Value.TryPickT2(out var inLibrary, out _))
        {
            return inLibrary.Equals(other.Value.AsT2);
        }

        if (Value.TryPickT3(out var installed, out _))
        {
            return installed.Equals(other.Value.AsT3);
        }

        throw new UnreachableException();
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

file class DatomEntityIdEqualityComparer : IEqualityComparer<Datom>
{
    public static readonly IEqualityComparer<Datom> Instance = new DatomEntityIdEqualityComparer();

    public bool Equals(Datom x, Datom y)
    {
        return x.E == y.E;
    }

    public int GetHashCode(Datom obj)
    {
        return obj.E.GetHashCode();
    }
}

internal class OptionalDatomComparer : IEqualityComparer<Optional<Datom>>
{
    public static readonly IEqualityComparer<Optional<Datom>> Instance = new OptionalDatomComparer();

    public bool Equals(Optional<Datom> x, Optional<Datom> y)
    {
        var (a, b) = (x.HasValue, y.HasValue);
        return (a, b) switch
        {
            (false, false) => true,
            (false, true) => false,
            (true, false) => false,
            (true, true) => x.Value.E.Equals(y.Value.E),
        };
    }

    public int GetHashCode(Optional<Datom> datom)
    {
        return datom.GetHashCode();
    }
}
