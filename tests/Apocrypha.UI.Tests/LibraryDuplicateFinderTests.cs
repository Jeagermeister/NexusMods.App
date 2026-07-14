using FluentAssertions;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.Pages.Library;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.UI.Tests;

/// <summary>
/// Directed tests for the "Remove duplicates" selection pipeline (CODE_REVIEW.md §7 #12, tb3):
/// it feeds a destructive deletion, so the keep-oldest / keep-linked / exclusion branches all get
/// coverage against a real MnemonicDB.
/// </summary>
public class LibraryDuplicateFinderTests : AUiTest
{
    private readonly IConnection _connection;

    public LibraryDuplicateFinderTests(IServiceProvider provider) : base(provider)
    {
        _connection = Connection;
    }

    private static LibraryFile.New MakeFile(ITransaction tx, string name, string content, out EntityId id) => new(tx, out id)
    {
        FileName = name,
        Hash = content.xxHash3AsUtf8(),
        Size = Size.FromLong(content.Length),
        LibraryItem = new LibraryItem.New(tx, id)
        {
            Name = name,
        },
    };

    /// <summary>
    /// Links a library item into a (synthetic) loadout group — the finder only checks the
    /// LibraryLinkedLoadoutItem backref, so a minimal group suffices.
    /// </summary>
    private static void Link(ITransaction tx, EntityId libraryItemId)
    {
        var group = new LoadoutItemGroup.New(tx, out var groupId)
        {
            IsGroup = true,
            LoadoutItem = new LoadoutItem.New(tx, groupId)
            {
                Name = "link",
                // The finder never dereferences the loadout; any entity id satisfies the reference.
                LoadoutId = LoadoutId.From(libraryItemId),
            },
        };
        _ = new LibraryLinkedLoadoutItem.New(tx, groupId)
        {
            LoadoutItemGroup = group,
            LibraryItemId = LibraryItemId.From(libraryItemId),
        };
    }

    [Fact]
    public async Task UnlinkedDuplicates_KeepOldest_RemoveRest()
    {
        var content = Guid.NewGuid().ToString();
        using var tx = _connection.BeginTransaction();
        MakeFile(tx, "a.zip", content, out var a);
        MakeFile(tx, "b.zip", content, out var b);
        MakeFile(tx, "c.zip", content, out var c);
        var result = await tx.Commit();

        var removable = LibraryDuplicateFinder.FindRemovableDuplicates(_connection.Db)
            .Where(item => item.Id == result[a] || item.Id == result[b] || item.Id == result[c])
            .ToArray();

        // All three are unlinked: the oldest (lowest id) is kept, two are removable.
        removable.Should().HaveCount(2);
        var keptId = new[] { result[a], result[b], result[c] }.Min();
        removable.Should().NotContain(item => item.Id == keptId);
    }

    [Fact]
    public async Task LinkedCopy_IsAlwaysKept_UnlinkedCopyRemoved()
    {
        var content = Guid.NewGuid().ToString();
        using var tx = _connection.BeginTransaction();
        MakeFile(tx, "linked.zip", content, out var linked);
        MakeFile(tx, "unlinked.zip", content, out var unlinked);
        Link(tx, linked);
        var result = await tx.Commit();

        var removable = LibraryDuplicateFinder.FindRemovableDuplicates(_connection.Db)
            .Where(item => item.Id == result[linked] || item.Id == result[unlinked])
            .ToArray();

        // The linked copy must survive even though it is older; ONLY the unlinked copy goes.
        removable.Should().ContainSingle().Which.Id.Should().Be(result[unlinked]);
    }

    [Fact]
    public async Task AllCopiesLinked_NothingRemoved()
    {
        var content = Guid.NewGuid().ToString();
        using var tx = _connection.BeginTransaction();
        MakeFile(tx, "one.zip", content, out var one);
        MakeFile(tx, "two.zip", content, out var two);
        Link(tx, one);
        Link(tx, two);
        var result = await tx.Commit();

        LibraryDuplicateFinder.FindRemovableDuplicates(_connection.Db)
            .Should().NotContain(item => item.Id == result[one] || item.Id == result[two]);
    }

    [Fact]
    public async Task DistinctContent_IsNotADuplicate()
    {
        using var tx = _connection.BeginTransaction();
        MakeFile(tx, "x.zip", Guid.NewGuid().ToString(), out var x);
        MakeFile(tx, "y.zip", Guid.NewGuid().ToString(), out var y);
        var result = await tx.Commit();

        LibraryDuplicateFinder.FindRemovableDuplicates(_connection.Db)
            .Should().NotContain(item => item.Id == result[x] || item.Id == result[y]);
    }

    [Fact]
    public async Task NestedArchiveEntries_AreNotDuplicatesOfDownloads()
    {
        // Identical files INSIDE archives (e.g. the icon.png every Thunderstore package ships)
        // must not count as duplicates of anything.
        var content = Guid.NewGuid().ToString();
        using var tx = _connection.BeginTransaction();

        MakeFile(tx, "download.zip", content, out var topLevel);

        var archiveFile = MakeFile(tx, "parent.zip", Guid.NewGuid().ToString(), out var archiveId);
        var archive = new LibraryArchive.New(tx, archiveId)
        {
            IsArchive = true,
            LibraryFile = archiveFile,
        };
        var nested = MakeFile(tx, "nested.png", content, out var nestedId);
        _ = new LibraryArchiveFileEntry.New(tx, nestedId)
        {
            Path = RelativePath.FromUnsanitizedInput("icon.png"),
            ParentId = archive,
            LibraryFile = nested,
        };

        var result = await tx.Commit();

        LibraryDuplicateFinder.FindRemovableDuplicates(_connection.Db)
            .Should().NotContain(item => item.Id == result[topLevel] || item.Id == result[nestedId],
                "a nested archive entry sharing content with a top-level download is not a removable duplicate");
    }
}
