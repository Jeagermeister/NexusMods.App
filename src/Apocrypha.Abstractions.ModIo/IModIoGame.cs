namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// Capability of a game whose mods are distributed through mod.io: lets source-agnostic UI
/// (e.g. the Library page's "get mods" entries) discover and link the game's mod.io hub
/// without referencing the game module. Deliberately a single narrow capability, not a
/// general mod-source interface (DESIGN-modsources.md §3, DESIGN-modio.md §5).
/// </summary>
public interface IModIoGame
{
    /// <summary>
    /// The game's URL slug on mod.io (e.g. <c>baldursgate3</c>, <c>readyornot</c>).
    /// </summary>
    string ModIoGameNameId { get; }
}
