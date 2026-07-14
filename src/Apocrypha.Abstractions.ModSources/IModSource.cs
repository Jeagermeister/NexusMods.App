using DynamicData.Kernel;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Abstractions.ModSources;

/// <summary>
/// A registered mod source (Nexus Mods, Thunderstore, mod.io, …) expressed as a capability
/// rather than a hardcoded per-source property. Source-agnostic consumers — the Library page's
/// "get mods" entries, the delete-warning UX, and (over time) the Downloads page — enumerate
/// <see cref="IModSource"/> from DI (<c>GetServices&lt;IModSource&gt;()</c>) instead of naming
/// each source, so a new source is one registration rather than an edit to every consumer.
///
/// This is deliberately a *narrow* capability, not a god interface: it covers "does this game
/// have this source, and where do I send the user to browse it". Downloading, metadata, and
/// protocol handling stay on their own existing seams (the per-source library facades, the
/// MnemonicDB metadata models, and <c>IIpcProtocolHandler</c>) — see DESIGN-modsources.md §3.
/// </summary>
public interface IModSource
{
    /// <summary>Stable internal identifier used to find a specific source among the registered set.</summary>
    ModSourceId Id { get; }

    /// <summary>Human-facing name of the source (e.g. "Nexus Mods", "Thunderstore", "mod.io").</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether the source is currently usable. Most sources are always enabled; a source gated
    /// behind an experimental setting (mod.io) reports its gate here, so consumers can hide it
    /// without knowing which setting drives it.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Whether <paramref name="game"/>'s mods are distributed through this source (i.e. the game
    /// declares the source's capability, such as a Nexus game id or a Thunderstore community).
    /// </summary>
    bool SupportsGame(IGameData game);

    /// <summary>
    /// The website URL to send the user to browse this source's mods for <paramref name="game"/>,
    /// or <see cref="Optional{T}.None"/> when the source doesn't support the game or can't build a
    /// URL for it. Only meaningful when <see cref="SupportsGame"/> is <c>true</c>.
    /// </summary>
    Optional<Uri> GetBrowseUri(IGameData game);
}
