using FluentAssertions;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Collections;
using NexusMods.MnemonicDB.Abstractions;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.Tests;

/// <summary>
/// Offline coverage for the collection status logic that the whole install pipeline branches on:
/// <see cref="CollectionDownloader.GetItems"/> decides what gets downloaded and installed, and
/// <see cref="CollectionDownloader.GetStatus"/> decides what gets skipped as already done.
///
/// <para>
/// <c>Apocrypha.Collections.Tests</c> has no offline tests at all — its only test needs a premium
/// Nexus account and live revisions — so none of this ran in CI. These use the recorded datastore
/// instead: a real Stardew Valley collection (<c>g14kxi</c> revision 42, 110 downloads) installed
/// into a real loadout, with a second collection added and removed.
/// </para>
/// </summary>
public class OfflineCollectionStatusTests(ITestOutputHelper helper) : AArchivedDatabaseTest(helper)
{
    private const string Fixture = "two_sdv_collections_added_removed.zip";

    /// <summary>
    /// Required and optional partition the revision exactly: every download is one or the other,
    /// and nothing is counted twice. The download pipeline drives off these two sets, so an overlap
    /// or a gap would silently either double-download or skip mods.
    /// </summary>
    [Fact]
    public async Task RequiredAndOptionalItemsPartitionTheRevision()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var revision = SingleRevision(tmp.Connection.Db);

        var required = CollectionDownloader.GetItems(revision, CollectionDownloader.ItemType.Required);
        var optional = CollectionDownloader.GetItems(revision, CollectionDownloader.ItemType.Optional);

        required.Should().HaveCount(96);
        optional.Should().HaveCount(14);
        (required.Length + optional.Length).Should().Be(revision.Downloads.Count);

        var requiredIds = required.Select(static item => item.Id).ToHashSet();
        var optionalIds = optional.Select(static item => item.Id).ToHashSet();
        requiredIds.Overlaps(optionalIds).Should().BeFalse();
    }

    /// <summary>
    /// The aggregate the install job trusts: this collection really was installed, so every required
    /// item reports installed and the collection reports fully installed and fully downloaded.
    /// </summary>
    [Fact]
    public async Task AnInstalledCollectionReportsFullyInstalled()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var db = tmp.Connection.Db;
        var group = SingleCollectionGroup(db);
        var required = CollectionDownloader.GetItems(group.Revision, CollectionDownloader.ItemType.Required);

        var notInstalled = required
            .Where(item => !CollectionDownloader.GetStatus(item, group.AsCollectionGroup(), db).IsInstalled(out _))
            .ToArray();

        notInstalled.Should().BeEmpty();

        CollectionDownloader.IsFullyInstalled(required, group.AsCollectionGroup(), db).Should().BeTrue();
        CollectionDownloader.IsFullyDownloaded(required, db).Should().BeTrue();
    }

    /// <summary>
    /// "Installed" is a question about a specific collection group, not about the library. Asked
    /// without a group, an item that is present in the library must report in-library — otherwise
    /// installing the same mod into a second collection would be skipped as already done.
    /// </summary>
    [Fact]
    public async Task WithoutACollectionGroupAnItemIsInLibraryRatherThanInstalled()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var db = tmp.Connection.Db;
        var required = CollectionDownloader.GetItems(SingleRevision(db), CollectionDownloader.ItemType.Required);

        var statuses = required.Select(item => CollectionDownloader.GetStatus(item, db)).ToArray();

        statuses.Count(status => status.IsInstalled(out _)).Should().Be(0);
        statuses.Should().Contain(status => status.IsDownloaded());
    }

    /// <summary>
    /// CHARACTERISATION of the detection half of review finding S5-1. Still true after the S5-1 fix,
    /// and deliberately so — see the remarks.
    ///
    /// <para>
    /// <see cref="CollectionDownloader.GetStatus"/> decides "installed" purely from a
    /// <see cref="LibraryLinkedLoadoutItem"/> parented to the collection group. It never checks that
    /// the group carries its <see cref="NexusCollectionItemLoadoutGroup"/> tag, so a stranded group is
    /// indistinguishable from a healthy one.
    /// </para>
    ///
    /// <para>
    /// The install job no longer *creates* that state: a failure after the group is committed now
    /// removes the group (see <see cref="CollectionDownloader.RetractStrandedItemGroup"/> and
    /// <see cref="RetractingAStrandedGroupRestoresARetryableState_S5_1"/>). Making status itself
    /// tag-aware would close the remaining window — a crash between the commit and the retract — but
    /// cannot be done in isolation: migration <c>_0002_NexusCollectionItem</c> calls this very method
    /// to decide which pre-tag items to tag, so requiring the tag here would make that migration tag
    /// nothing and silently orphan every existing user's collection items. Tracked as follow-up.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnUntaggedGroupStillReportsInstalled_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);

        // Pick a tagged child and find the download it came from.
        var tagged = NexusCollectionItemLoadoutGroup
            .All(conn.Db)
            .First(item => item.AsLoadoutItemGroup().AsLoadoutItem().ParentId.Value == group.Id);

        var download = CollectionDownload.Load(conn.Db, tagged.DownloadId);
        CollectionDownloader.GetStatus(download, group.AsCollectionGroup(), conn.Db)
            .IsInstalled(out _)
            .Should().BeTrue("this item really is installed to begin with");

        // Strip the tag, leaving the deployed group and its library link in place. This is the state
        // a curator-patch failure leaves behind.
        using (var tx = conn.BeginTransaction())
        {
            tx.Retract(tagged.Id, NexusCollectionItemLoadoutGroup.Download, tagged.DownloadId.Value);
            tx.Retract(tagged.Id, NexusCollectionItemLoadoutGroup.IsRequired, tagged.IsRequired);
            await tx.Commit();
        }

        var db = conn.Db;
        NexusCollectionItemLoadoutGroup.Load(db, tagged.Id).IsValid()
            .Should().BeFalse("the tag is what we just removed");

        CollectionDownloader.GetStatus(CollectionDownload.Load(db, tagged.DownloadId), group.AsCollectionGroup(), db)
            .IsInstalled(out _)
            .Should().BeTrue("S5-1: status keys on the parent link, not the tag, so a stranded group is indistinguishable from a healthy one");
    }

    /// <summary>
    /// The S5-1 fix: the compensating action really does return a stranded item to a state a retry can
    /// heal. Removing the group flips the download from installed back to in-library — which is what
    /// the install job needs to see, since it skips anything already reporting installed.
    /// </summary>
    [Fact]
    public async Task RetractingAStrandedGroupRestoresARetryableState_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);

        var tagged = NexusCollectionItemLoadoutGroup
            .All(conn.Db)
            .First(item => item.AsLoadoutItemGroup().AsLoadoutItem().ParentId.Value == group.Id);

        var downloadId = tagged.DownloadId;
        CollectionDownloader.GetStatus(CollectionDownload.Load(conn.Db, downloadId), group.AsCollectionGroup(), conn.Db)
            .IsInstalled(out _)
            .Should().BeTrue("this item really is installed to begin with");

        await CollectionDownloader.RetractStrandedItemGroup(conn, tagged.Id);

        var db = conn.Db;
        var status = CollectionDownloader.GetStatus(CollectionDownload.Load(db, downloadId), group.AsCollectionGroup(), db);

        status.IsInstalled(out _)
            .Should().BeFalse("retracting the group is what makes a failed install retryable instead of permanently stranded");
        status.IsDownloaded()
            .Should().BeTrue("the library item is untouched — only the loadout group was removed, so the retry does not re-download");
    }

    /// <summary>
    /// The retract has to take the group's files with it. Leaving them parented to a deleted group
    /// would have the retry install a second copy over the half-installed remains.
    /// </summary>
    [Fact]
    public async Task RetractingAStrandedGroupRemovesItsFiles_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);

        var tagged = NexusCollectionItemLoadoutGroup
            .All(conn.Db)
            .First(item =>
                item.AsLoadoutItemGroup().AsLoadoutItem().ParentId.Value == group.Id &&
                item.AsLoadoutItemGroup().Children.Count > 0);

        var childIds = tagged.AsLoadoutItemGroup().Children.Select(static child => child.Id).ToArray();
        childIds.Should().NotBeEmpty("picked a group that actually deployed files");

        await CollectionDownloader.RetractStrandedItemGroup(conn, tagged.Id);

        var db = conn.Db;
        LoadoutItem.Load(db, tagged.Id).IsValid().Should().BeFalse("the group itself is gone");
        childIds.Where(id => LoadoutItem.Load(db, id).IsValid())
            .Should().BeEmpty("every file under the group went with it");
    }

    private static CollectionRevisionMetadata.ReadOnly SingleRevision(IDb db)
    {
        return CollectionRevisionMetadata.All(db).Single();
    }

    private static NexusCollectionLoadoutGroup.ReadOnly SingleCollectionGroup(IDb db)
    {
        return NexusCollectionLoadoutGroup.All(db).Single();
    }
}
