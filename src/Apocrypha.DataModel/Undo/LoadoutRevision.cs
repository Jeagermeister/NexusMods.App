using NexusMods.MnemonicDB.Abstractions;

namespace Apocrypha.DataModel.Undo;

/// <summary>
/// Row definition for the LoadoutRevision entity.
/// </summary>
public partial record struct LoadoutRevision(EntityId EntityId, EntityId TxEntity, DateTimeOffset Timestamp);

public partial record struct LoadoutRevisionWithStats(LoadoutRevision Revision, int ModCount);
