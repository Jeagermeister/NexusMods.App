using NexusMods.MnemonicDB.Abstractions;

namespace Apocrypha.DataModel.SchemaVersions;

public interface ITransactionalMigration : IMigration
{
    /// <summary>
    /// Run the migration inserting changes into the given transaction
    /// </summary>
    public void Migrate(ITransaction tx, IDb db);
}
