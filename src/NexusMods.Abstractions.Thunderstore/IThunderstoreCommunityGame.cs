namespace NexusMods.Abstractions.Thunderstore;

/// <summary>
/// Capability of a game that has a Thunderstore community: lets source-agnostic UI
/// (e.g. the Library page's "get mods" entries, DESIGN-app-layout.md §5) discover and
/// link the community without referencing the game module. Implemented by games whose
/// mods are distributed through Thunderstore; deliberately a single narrow capability,
/// not a general mod-source interface (DESIGN-modsources.md §3).
/// </summary>
public interface IThunderstoreCommunityGame
{
    /// <summary>
    /// The community's URL slug on thunderstore.io (e.g. <c>subnautica</c>, <c>riskofrain2</c>).
    /// </summary>
    string ThunderstoreCommunitySlug { get; }
}
