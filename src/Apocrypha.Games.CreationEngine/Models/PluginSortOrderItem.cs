using JetBrains.Annotations;
using Apocrypha.Abstractions.Games;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace Apocrypha.Games.CreationEngine.Models;

/// <summary>
/// A single entry in a Creation Engine plugin load order.
/// </summary>
[PublicAPI]
[Include<SortOrderItem>]
public partial class PluginSortOrderItem : IModelDefinition
{
    // Persisted MnemonicDB attribute namespace, shipped in v0.3.0 datastores. It deliberately
    // does NOT match the class name — renaming it (or "fixing" the branding) breaks existing
    // installs; see the schema-stability rules in CLAUDE.md.
    private const string Namespace = "NexusMods.Games.CreationEngine.PluginSortableEntry";

    /// <summary>
    /// The plugin file name this entry orders (e.g. `MyMod.esp`), in display casing. Plugin file
    /// names are case-insensitive identifiers to the engine, so all matching against loadout
    /// contents folds case; this stores whichever casing the entry was created with.
    /// </summary>
    public static readonly StringAttribute PluginName = new(Namespace, nameof(PluginName));
}
