using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.References;
using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.App.UI.DiagnosticSystem;

internal sealed class LoadoutReferenceFormatter(IConnection conn) : IValueFormatter<LoadoutReference>
{
    public void Format(IDiagnosticWriter writer, ref DiagnosticWriterState state, LoadoutReference value)
    {
        // TODO: custom markdown control
        var loadout = Loadout.Load(conn.Db, value.DataId);
        if (loadout.IsValid())
        {
            writer.Write(ref state, loadout.Name);
        }
        else
        {
            writer.Write(ref state, $"Invalid Loadout entity: {value.DataId}");
        }
    }
}
