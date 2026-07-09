using JetBrains.Annotations;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Diagnostics.References;
using NexusMods.Abstractions.Diagnostics.Values;
using NexusMods.Generators.Diagnostics;

namespace NexusMods.Games.BepInEx;

internal static partial class Diagnostics
{
    private const string Source = "NexusMods.Games.BepInEx";

    [DiagnosticTemplate]
    [UsedImplicitly]
    internal static IDiagnosticTemplate MissingBepInExTemplate = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(Source, number: 1))
        .WithTitle("BepInEx is not installed")
        .WithSeverity(DiagnosticSeverity.Warning)
        .WithSummary("{PluginCount} mod(s) require BepInEx, which is not installed")
        .WithDetails("""
The mods in this loadout are BepInEx plugins, but the BepInEx loader pack itself is not part of the loadout, so the game will start without any mods.

You can get a BepInEx pack from {BepInExPackUri} — download it with "Install with Mod Manager", then install it from your Library into this loadout.
""")
        .WithMessageData(messageBuilder => messageBuilder
            .AddValue<int>("PluginCount")
            .AddValue<NamedLink>("BepInExPackUri")
        )
        .Finish();
}
