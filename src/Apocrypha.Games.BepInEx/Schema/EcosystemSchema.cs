using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Games.BepInEx.Schema;

/// <summary>
/// DTOs for the Thunderstore ecosystem schema (the dataset r2modman is driven by),
/// vendored at <c>Assets/ecosystem-schema.json</c>. Only the fields this app consumes are
/// modeled; unknown fields are ignored. See DESIGN-bepinex-family.md §2.3/§4.
/// </summary>
[PublicAPI]
public class EcosystemSchema
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("games")]
    public required Dictionary<string, EcosystemGame> Games { get; init; }

    /// <summary>
    /// Keyed by community slug. Absent for the 46 legacy games whose community predates the
    /// schema's community blocks.
    /// </summary>
    [JsonPropertyName("communities")]
    public Dictionary<string, EcosystemCommunity> Communities { get; init; } = new();
}

[PublicAPI]
public class EcosystemCommunity
{
    [JsonPropertyName("meta")]
    public EcosystemCommunityMeta? Meta { get; init; }
}

[PublicAPI]
public class EcosystemCommunityMeta
{
    /// <summary>The community's 192×192 icon, relative to <c>https://gcdn.thunderstore.io/assets/</c>.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
}

[PublicAPI]
public class EcosystemGame
{
    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("meta")]
    public required EcosystemMeta Meta { get; init; }

    /// <summary>
    /// One entry per manager-supported instance (game client, dedicated server).
    /// Null for games that have a Thunderstore community but no mod-manager support.
    /// </summary>
    [JsonPropertyName("r2modman")]
    public List<EcosystemGameInstance>? R2modman { get; init; }
}

[PublicAPI]
public class EcosystemMeta
{
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>The game's 360×480 cover, relative to <c>https://gcdn.thunderstore.io/assets/</c>.</summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; init; }
}

[PublicAPI]
public class EcosystemGameInstance
{
    [JsonPropertyName("meta")]
    public required EcosystemMeta Meta { get; init; }

    [JsonPropertyName("dataFolderName")]
    public string? DataFolderName { get; init; }

    /// <summary>Per-instance store identifiers — authoritative (the game-level list is often empty).</summary>
    [JsonPropertyName("distributions")]
    public List<EcosystemDistribution> Distributions { get; init; } = [];

    /// <summary>Unique across all instances in the schema — the stable key for this family.</summary>
    [JsonPropertyName("settingsIdentifier")]
    public required string SettingsIdentifier { get; init; }

    /// <summary>
    /// <c>https://thunderstore.io/c/{community}/api/v1/package-listing-index/</c> — the only
    /// reliable source of the community slug (46 legacy games have no community block).
    /// </summary>
    [JsonPropertyName("packageIndex")]
    public required string PackageIndex { get; init; }

    [JsonPropertyName("steamFolderName")]
    public string? SteamFolderName { get; init; }

    [JsonPropertyName("exeNames")]
    public List<string> ExeNames { get; init; } = [];

    /// <summary>"game" or "server".</summary>
    [JsonPropertyName("gameInstanceType")]
    public required string GameInstanceType { get; init; }

    /// <summary>"visible" or "hidden".</summary>
    [JsonPropertyName("gameSelectionDisplayMode")]
    public string GameSelectionDisplayMode { get; init; } = "visible";

    [JsonPropertyName("packageLoader")]
    public required string PackageLoader { get; init; }

    [JsonPropertyName("installRules")]
    public List<EcosystemInstallRule> InstallRules { get; init; } = [];

    [JsonPropertyName("relativeFileExclusions")]
    public List<string>? RelativeFileExclusions { get; init; }
}

[PublicAPI]
public class EcosystemDistribution
{
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>Nullable — <c>platform: "other"</c> entries can carry no identifier.</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }
}

[PublicAPI]
public class EcosystemInstallRule
{
    [JsonPropertyName("route")]
    public required string Route { get; init; }

    [JsonPropertyName("defaultFileExtensions")]
    public List<string> DefaultFileExtensions { get; init; } = [];

    /// <summary>subdir | none | state | subdir-no-flatten | package-zip.</summary>
    [JsonPropertyName("trackingMethod")]
    public required string TrackingMethod { get; init; }

    [JsonPropertyName("subRoutes")]
    public List<EcosystemInstallRule> SubRoutes { get; init; } = [];

    [JsonPropertyName("isDefaultLocation")]
    public bool IsDefaultLocation { get; init; }
}
