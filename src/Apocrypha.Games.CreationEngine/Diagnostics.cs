using JetBrains.Annotations;
using Mutagen.Bethesda.Plugins;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.References;
using Apocrypha.Abstractions.Diagnostics.Values;
using Apocrypha.Generators.Diagnostics;

namespace Apocrypha.Games.CreationEngine;

internal static partial class Diagnostics
{
    private const string Source = "Apocrypha.Games.CreationEngine";

    [DiagnosticTemplate]
    [UsedImplicitly]
    internal static IDiagnosticTemplate MissingMaster = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(Source, number: 1))
        .WithTitle("Missing Master")
        .WithSeverity(DiagnosticSeverity.Critical)
        .WithSummary("{PluginName} requires {MissingMasterPluginName} which is not installed")
        .WithDetails("""
The mod **{PluginName}** requires **{MissingMasterPluginName}** to function, but **{MissingMasterPluginName}** is not provided by any mods.

This means the plugin can’t find one of its required “master files.” Masters contain important data that other mods depend on—without them, the plugin is left pointing to records that don’t exist.

If you try to launch the game in this state, it won’t start. Bethesda games stop loading when a master is missing to prevent crashes, broken saves, or corrupted load orders. 
To fix this, you’ll need to install the missing master before playing.
""")
        .WithMessageData(messageBuilder => messageBuilder
            .AddDataReference<LoadoutItemGroupReference>("Mod")
            .AddValue<ModKey>("PluginName")
            .AddValue<ModKey>("MissingMasterPluginName")
        )
        .Finish();

    
    [DiagnosticTemplate]
    [UsedImplicitly]
    internal static IDiagnosticTemplate MissingMasterIsDisabled = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(Source, number: 2))
        .WithTitle("Disabled Master")
        .WithSeverity(DiagnosticSeverity.Critical)
        .WithSummary("{PluginName} requires {MissingMasterPluginName} which is installed in {MissingMasterMod} but is disabled")
        .WithDetails("""
The mod **{PluginName}** requires **{MissingMasterPluginName}** to function, but it is not provided by any enabled mods. The 
mod **{MissingMasterMod}** provides this file, but it is disabled. Enable the mod to fix this issue.

This means the plugin can’t find one of its required “master files.” Masters contain important data that other mods depend on—without them, the plugin is left pointing to records that don’t exist.

If you try to launch the game in this state, it won’t start. Bethesda games stop loading when a master is missing to prevent crashes, broken saves, or corrupted load orders. 
To fix this, you’ll need to install the missing master before playing.
""")
        .WithMessageData(messageBuilder => messageBuilder
            .AddDataReference<LoadoutItemGroupReference>("Mod")
            .AddValue<ModKey>("PluginName")
            .AddValue<ModKey>("MissingMasterPluginName")
            .AddValue<LoadoutItemGroupReference>("MissingMasterMod")
        )
        .Finish();


    [DiagnosticTemplate]
    [UsedImplicitly]
    internal static IDiagnosticTemplate SaveBreakingLoadOrderChange = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(Source, number: 3))
        .WithTitle("Load order change will break existing saves")
        .WithSeverity(DiagnosticSeverity.Warning)
        .WithSummary("{SaveCount} existing save(s) were made with a different set of plugins ({AddedCount} added, {RemovedCount} removed)")
        .WithDetails("""
Applying this loadout changes which plugins are loaded, and you have **{SaveCount}** save(s) for this game.

Added: {AddedPlugins}

Removed: {RemovedPlugins}

A Creation Engine save stores every object it knows about by *load order position*, not by name. Adding
or removing a plugin shifts the positions of the plugins after it, so a save written before the change
resolves its objects to the wrong plugins. In practice the game either refuses to load the save or
crashes shortly after you select it.

New games started after applying are unaffected. If you want to keep playing an existing save, restore
the previous set of plugins first — matching the plugins the save was made with is what matters, not
the mods' content.
""")
        .WithMessageData(messageBuilder => messageBuilder
            .AddValue<int>("SaveCount")
            .AddValue<int>("AddedCount")
            .AddValue<int>("RemovedCount")
            .AddValue<string>("AddedPlugins")
            .AddValue<string>("RemovedPlugins")
        )
        .Finish();


    [DiagnosticTemplate]
    [UsedImplicitly]
    internal static IDiagnosticTemplate TooManyArchives = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(Source, number: 4))
        .WithTitle("Too many archives for the engine")
        .WithSeverity(DiagnosticSeverity.Warning)
        .WithSummary("{ArchiveCount} archives are loaded by enabled plugins, above the engine's limit of {ArchiveLimit}")
        .WithDetails("""
Enabled plugins load **{ArchiveCount}** archives, but this engine can only handle **{ArchiveLimit}** at once.

The game will still launch — the failures happen later and look unrelated: purple or garbage textures, missing meshes and sounds, or a crash at the main menu with an access violation deep in texture code. Nothing in the game reports the real cause.

Current state: {CurrentMitigation}

The most effective fix is **Buffout 4** with both of these set in `Data/F4SE/Plugins/Buffout4/config.toml`:

- `ArchiveLimit = true` — bypasses the archive ceiling
- `MaxStdIO = 2048` — raises the C-runtime's file-handle cap from its 512 default (2048 is the maximum the old runtime accepts; **larger values crash the game silently on startup**)

Alternatively, disable enough archive-loading mods to get under the limit.

Once both Buffout settings are in place, this warning goes away on its own.
""")
        .WithMessageData(messageBuilder => messageBuilder
            .AddValue<int>("ArchiveCount")
            .AddValue<int>("ArchiveLimit")
            .AddValue<string>("CurrentMitigation")
        )
        .Finish();
}
