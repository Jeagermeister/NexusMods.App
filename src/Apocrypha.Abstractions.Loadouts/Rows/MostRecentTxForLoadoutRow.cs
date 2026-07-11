using NexusMods.MnemonicDB.Abstractions;

namespace Apocrypha.Abstractions.Loadouts.Rows;

public readonly partial record struct MostRecentTxForLoadoutRow(EntityId LoadoutId, EntityId TxId, int ItemCount);
