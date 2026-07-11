using System.Collections.Immutable;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Apocrypha.Games.BepInEx.Schema;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Games.BepInEx;

/// <summary>
/// Everything the family needs to stand up one BepInEx game — the "data row" a
/// <see cref="GenericBepInExGame"/> is constructed from. Produced by
/// <see cref="Schema.EcosystemSchemaParser"/> from the vendored ecosystem schema plus the
/// generated Nexus-id mapping. See DESIGN-bepinex-family.md §3/§5.
/// </summary>
[PublicAPI]
public record BepInExGameData
{
    /// <summary>
    /// The schema's <c>settingsIdentifier</c> — unique across all instances, and the string
    /// the family <see cref="GameId"/> is minted from. (The hand-written RoR2 module's
    /// GameId "RiskOfRain2" already equals its settingsIdentifier, so its PR G migration
    /// into the family is identity-preserving.)
    /// </summary>
    public required string SettingsIdentifier { get; init; }

    public required string DisplayName { get; init; }

    public required GameId GameId { get; init; }

    /// <summary>
    /// The game's Nexus Mods id when it is dual-source (most BepInEx games are); None for
    /// Thunderstore-exclusive games, which take the PR D zero-sentinel path.
    /// </summary>
    public required Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId { get; init; }

    public required ImmutableArray<uint> SteamAppIds { get; init; }

    /// <summary>The Windows executable used as the game's primary file (runs under Proton).</summary>
    public required string PrimaryExeName { get; init; }

    /// <summary>All known executable names; entries ending .x86/.x86_64 mark native Linux builds.</summary>
    public required ImmutableArray<string> ExeNames { get; init; }

    /// <summary>The Thunderstore community slug, derived from the schema's packageIndex URL.</summary>
    public required string CommunitySlug { get; init; }

    /// <summary>The game's 360×480 cover, relative to <c>https://gcdn.thunderstore.io/assets/</c> — the tile image.</summary>
    public string? CoverUrl { get; init; }

    /// <summary>
    /// The community's 192×192 icon, relative to <c>https://gcdn.thunderstore.io/assets/</c> —
    /// the thumbnail/spine image. Null where the community has no icon (legacy communities).
    /// </summary>
    public string? CommunityIconUrl { get; init; }

    /// <summary>The Unity data folder name (empty for non-Unity games); future backup-ignore input.</summary>
    public string? DataFolderName { get; init; }

    /// <summary>
    /// The schema's install rules for this game. Unused in PR E (the canonical installer
    /// behavior applies); the PR F rule engine interprets them.
    /// </summary>
    public required IReadOnlyList<EcosystemInstallRule> InstallRules { get; init; }

    /// <summary>Files never deployed from packages, per the schema (beyond the standard metadata skip-list).</summary>
    public IReadOnlyList<string>? RelativeFileExclusions { get; init; }
}
