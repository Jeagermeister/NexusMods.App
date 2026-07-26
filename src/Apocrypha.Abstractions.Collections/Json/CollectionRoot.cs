using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Collections.Json;

/// <summary>
/// DTO representing the `collection.json` file
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class CollectionRoot
{
    [JsonPropertyName("info")]
    public required CollectionInfo Info { get; init; }
    
    [JsonPropertyName("mods")]
    public Mod[] Mods { get; init; } = [];

    /// <summary>
    /// Vortex-styled rules for "mods".
    /// </summary>
    [JsonPropertyName("modRules")]
    public ModRule[] ModRules { get; init; } = [];

    /// <summary>
    /// Plugins the collection ships, in the curator's intended order. Only present for
    /// Gamebryo/Creation Engine collections; empty for every other game.
    /// </summary>
    [JsonPropertyName("plugins")]
    public CollectionPlugin[] Plugins { get; init; } = [];

    /// <summary>
    /// LOOT-style ordering rules for <see cref="Plugins"/>. For a Creation Engine collection this
    /// is the curated load order, which master references alone cannot reconstruct.
    /// </summary>
    [JsonPropertyName("pluginRules")]
    public GamebryoPluginRules? PluginRules { get; init; }
}
