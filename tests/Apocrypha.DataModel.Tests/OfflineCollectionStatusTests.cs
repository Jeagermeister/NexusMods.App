using FluentAssertions;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Collections;
using Apocrypha.DataModel.SchemaVersions.Migrations;
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
    /// The detection half of review finding S5-1. A group that was committed and deployed but never
    /// claimed by the collection installer — the state a crash between the install commit and the
    /// tagging transaction leaves behind — must not report as installed, because the install job skips
    /// anything that does and no retry could then heal it.
    /// </summary>
    [Fact]
    public async Task AnUntaggedGroupNoLongerReportsInstalled_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);
        var tagged = TaggedNexusItems(conn.Db, group).First();

        var download = CollectionDownload.Load(conn.Db, tagged.DownloadId);
        CollectionDownloader.GetStatus(download, group.AsCollectionGroup(), conn.Db)
            .IsInstalled(out _)
            .Should().BeTrue("this item really is installed to begin with");

        await StripTag(conn, tagged, keepIsRequired: false);

        var db = conn.Db;
        NexusCollectionItemLoadoutGroup.Load(db, tagged.Id).IsValid()
            .Should().BeFalse("the tag is what we just removed");

        var status = CollectionDownloader.GetStatus(CollectionDownload.Load(db, tagged.DownloadId), group.AsCollectionGroup(), db);
        status.IsInstalled(out _)
            .Should().BeFalse("an unclaimed group is a failed install, and reporting it as installed is what made the state permanent");
        status.IsDownloaded()
            .Should().BeTrue("the library item is untouched, so the retry reinstalls rather than re-downloads");
    }

    /// <summary>
    /// The constraint that kept the fix above blocked, now covered instead of merely reasoned about.
    ///
    /// <para>
    /// Migration <c>_0002_NexusCollectionItem</c> backfills <see cref="NexusCollectionItemLoadoutGroup.IsRequired"/>
    /// alone for pre-tag items it cannot match to a download ("set a default value and hope for the
    /// best"), so a real legacy install can carry <c>IsRequired</c> with no <c>Download</c>. Requiring
    /// the full tag would report every one of those users' installed mods as merely in-library — and
    /// worse, offer them to the stranded-group sweep. Half-tagged is legacy-but-healthy; a crash leaves
    /// neither attribute.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ALegacyHalfTaggedGroupStillReportsInstalled_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);
        var tagged = TaggedNexusItems(conn.Db, group).First();

        await StripTag(conn, tagged, keepIsRequired: true);

        var db = conn.Db;
        NexusCollectionItemLoadoutGroup.Download.IsIn(LoadoutItem.Load(db, tagged.Id))
            .Should().BeFalse("this is the shape the migration's fallback branch leaves behind");

        CollectionDownloader.GetStatus(CollectionDownload.Load(db, tagged.DownloadId), group.AsCollectionGroup(), db)
            .IsInstalled(out _)
            .Should().BeTrue("a migration-backfilled item is a genuine install, not a stranded one");
    }

    /// <summary>
    /// Migration <c>_0002_NexusCollectionItem</c> asks status which pre-tag items to tag, so its view
    /// has to stay tag-blind or it would find nothing installed, tag nothing, and drop every item into
    /// its "hope for the best" branch. Same untagged item as
    /// <see cref="AnUntaggedGroupNoLongerReportsInstalled_S5_1"/>, opposite answer, on purpose.
    /// </summary>
    [Fact]
    public async Task TheMigrationsViewOfAnUntaggedGroupStaysInstalled_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);
        var tagged = TaggedNexusItems(conn.Db, group).First();

        await StripTag(conn, tagged, keepIsRequired: false);

        var db = conn.Db;
        CollectionDownloader.GetStatusIgnoringCollectionItemTag(CollectionDownload.Load(db, tagged.DownloadId), group.AsCollectionGroup(), db)
            .IsInstalled(out var found)
            .Should().BeTrue("the migration must still see the untagged item it exists to tag");
        found.Id.Should().Be(tagged.Id);
    }

    /// <summary>
    /// The migration end to end, against a real installed collection. No committed legacy snapshot
    /// contains a Nexus collection at all (every one of them has zero <see cref="NexusCollectionLoadoutGroup"/>
    /// entities), so this synthesises the pre-tag state from the recording — strip the tags, run the
    /// migration, expect every item claimed again — which is the only way to prove the tag-blind path
    /// really does keep upgrading users' collection items from being orphaned.
    /// </summary>
    [Fact]
    public async Task TheMigrationStillTagsEveryItemAfterTheStatusChange_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);

        var before = TaggedNexusItems(conn.Db, group)
            .ToDictionary(item => item.Id, item => (Download: item.DownloadId, item.IsRequired));
        before.Should().NotBeEmpty("the recording contains a real installed collection");

        using (var tx = conn.BeginTransaction())
        {
            foreach (var (id, tag) in before)
            {
                tx.Retract(id, NexusCollectionItemLoadoutGroup.Download, tag.Download.Value);
                tx.Retract(id, NexusCollectionItemLoadoutGroup.IsRequired, tag.IsRequired);
            }

            await tx.Commit();
        }

        var migration = new _0002_NexusCollectionItem();
        await migration.Prepare(conn.Db);
        using (var tx = conn.BeginTransaction())
        {
            migration.Migrate(tx, conn.Db);
            await tx.Commit();
        }

        var db = conn.Db;
        foreach (var (id, tag) in before)
        {
            var item = LoadoutItem.Load(db, id);
            NexusCollectionItemLoadoutGroup.Download.IsIn(item)
                .Should().BeTrue($"item {id} must be re-tagged rather than orphaned into the migration's fallback branch");

            var retagged = NexusCollectionItemLoadoutGroup.Load(db, id);
            retagged.DownloadId.Should().Be(tag.Download, "the migration must find the download the item actually came from");
            retagged.IsRequired.Should().Be(tag.IsRequired);
        }
    }

    /// <summary>
    /// The sweep that makes the detection change actually heal something. A crash — as opposed to a
    /// caught failure — never runs the compensating retract, so the remains are still there on the next
    /// attempt; without clearing them the retry would install a second group beside them. It has to be
    /// exact: a healthy group and a legacy half-tagged one must both survive.
    /// </summary>
    [Fact]
    public async Task SweepingRemovesCrashStrandedGroupsAndNothingElse_S5_1()
    {
        await using var tmp = await ConnectionFor(Fixture);
        var conn = tmp.Connection;
        var group = SingleCollectionGroup(conn.Db);

        var candidates = TaggedNexusItems(conn.Db, group).Take(3).ToArray();
        candidates.Should().HaveCount(3, "the fixture has plenty of installed Nexus items to pick from");
        var (stranded, legacy, healthy) = (candidates[0], candidates[1], candidates[2]);

        await StripTag(conn, stranded, keepIsRequired: false);
        await StripTag(conn, legacy, keepIsRequired: true);

        var downloads = group.Revision.Downloads.ToArray();
        var swept = await CollectionDownloader.RetractStrandedItemGroups(conn, downloads, group.AsCollectionGroup());

        swept.Should().BeEquivalentTo([stranded.Id], "only the unclaimed group is a failed install");

        var db = conn.Db;
        LoadoutItem.Load(db, stranded.Id).IsValid().Should().BeFalse("the stranded remains are gone, so the retry installs cleanly");
        LoadoutItem.Load(db, legacy.Id).IsValid().Should().BeTrue("a migration-backfilled item is not a failed install");
        LoadoutItem.Load(db, healthy.Id).IsValid().Should().BeTrue("a fully tagged item is obviously not a failed install");

        CollectionDownloader.GetStatus(CollectionDownload.Load(db, stranded.DownloadId), group.AsCollectionGroup(), db)
            .IsDownloaded()
            .Should().BeTrue("sweeping removes the loadout group only — the library item stays, so no re-download");
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

    /// <summary>
    /// Installed, tagged children of the collection group whose download is a Nexus one. Bundled
    /// downloads are deliberately excluded: their group carries a <c>BundleDownload</c> reference minted
    /// in the same transaction as the install, so status answers them without consulting the tag at all.
    /// </summary>
    private static NexusCollectionItemLoadoutGroup.ReadOnly[] TaggedNexusItems(IDb db, NexusCollectionLoadoutGroup.ReadOnly group)
    {
        return NexusCollectionItemLoadoutGroup
            .All(db)
            .Where(item => item.AsLoadoutItemGroup().AsLoadoutItem().ParentId.Value == group.Id)
            .Where(item => CollectionDownload.Load(db, item.DownloadId).TryGetAsCollectionDownloadNexusMods(out _))
            .ToArray();
    }

    /// <summary>
    /// Leaves the deployed group and its library link in place while removing what marks it as claimed
    /// by the collection installer. <paramref name="keepIsRequired"/> picks which shape:
    /// <c>false</c> is the crash window, <c>true</c> is a legacy item backfilled by migration
    /// <c>_0002_NexusCollectionItem</c>'s fallback branch.
    /// </summary>
    private static async Task StripTag(IConnection conn, NexusCollectionItemLoadoutGroup.ReadOnly item, bool keepIsRequired)
    {
        using var tx = conn.BeginTransaction();
        tx.Retract(item.Id, NexusCollectionItemLoadoutGroup.Download, item.DownloadId.Value);
        if (!keepIsRequired) tx.Retract(item.Id, NexusCollectionItemLoadoutGroup.IsRequired, item.IsRequired);
        await tx.Commit();
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
