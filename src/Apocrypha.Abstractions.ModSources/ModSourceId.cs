namespace Apocrypha.Abstractions.ModSources;

/// <summary>
/// Stable identifier for a mod source (Nexus Mods, Thunderstore, mod.io, …). Used by
/// source-agnostic consumers to look up a specific source among the registered set without
/// hardcoding a per-source property. The value is an internal slug, never shown to the user
/// (see <see cref="IModSource.DisplayName"/> for that).
/// </summary>
public readonly record struct ModSourceId(string Value)
{
    /// <inheritdoc cref="Value"/>
    public override string ToString() => Value;

    public static readonly ModSourceId NexusMods = new("nexusmods");
    public static readonly ModSourceId Thunderstore = new("thunderstore");
    public static readonly ModSourceId ModIo = new("modio");
}
