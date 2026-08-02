using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.DataModel.SchemaVersions.Migrations;

/// <summary>
/// Repairs <c>LoadoutItemWithTargetPath.TargetPath</c> rows whose first tuple element is not the
/// loadout id.
///
/// <para>
/// The replicated- and bundled-mod branches of <c>InstallCollectionDownloadJob</c> wrote the
/// file's own entity id into <c>TargetPath.Item1</c> (inherited from upstream). The synchronizer
/// filters on the <c>LoadoutItem.Loadout</c> attribute, so the files deployed and appeared in
/// plugins.txt — but every query that filters on <c>TargetPath.Item1</c> (the Creation Engine
/// and REDmod sort-order SQL) was blind to them: their plugins could not hold a curated load
/// order position and silently fell to the tail on every write. Observed live: 1,238 such rows
/// in a real Fallout 4 collection loadout.
/// </para>
///
/// <para>
/// The <c>LoadoutItem.Loadout</c> attribute is the source of truth (it is what the synchronizer
/// trusted all along); the repair rewrites <c>Item1</c> to match it. Idempotent: a repaired row
/// no longer matches the scan.
/// </para>
/// </summary>
public class _0010_FixCollectionTargetPaths : ITransactionalMigration
{
    public static (MigrationId Id, string Name) IdAndName { get; } = MigrationId.ParseNameAndId(nameof(_0010_FixCollectionTargetPaths));

    private readonly List<(EntityId Item, EntityId Loadout, LocationId Location, RelativePath Path)> _broken = [];

    public async Task Prepare(IDb db)
    {
        await Task.Yield();
        foreach (var loadout in Loadout.All(db))
        {
            foreach (var datom in db.Datoms(LoadoutItem.Loadout, loadout.LoadoutId))
            {
                var item = LoadoutItemWithTargetPath.Load(db, datom.E);
                if (!item.IsValid()) continue;

                EntityId tupleLoadout;
                LocationId location;
                RelativePath path;
                try
                {
                    (tupleLoadout, location, path) = item.TargetPath;
                }
                catch (PathException)
                {
                    // Real datastores contain rows whose path bytes no longer deserialize
                    // (encoding-mangled names, seen in a recorded test database). A row that
                    // cannot be read cannot be repaired, and it predates this migration --
                    // skip it rather than fail the whole migration.
                    continue;
                }

                if (tupleLoadout == loadout.Id) continue;

                _broken.Add((item.Id, loadout.Id, location, path));
            }
        }
    }

    public void Migrate(ITransaction tx, IDb db)
    {
        foreach (var (item, loadout, location, path) in _broken)
        {
            tx.Add(item, LoadoutItemWithTargetPath.TargetPath, (loadout, location, path));
        }
    }
}
