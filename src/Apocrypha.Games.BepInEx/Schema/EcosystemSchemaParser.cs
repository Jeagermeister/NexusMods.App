using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.NexusModsApi;

namespace Apocrypha.Games.BepInEx.Schema;

/// <summary>
/// Parses the vendored ecosystem schema + generated Nexus-id mapping into the family's
/// game rows. Runs once at DI-registration time (no services required).
/// Filtering, dedup, and validation rules per DESIGN-bepinex-family.md §4/§4b.
/// </summary>
[PublicAPI]
public static partial class EcosystemSchemaParser
{
    private const string SchemaResourceName = "Apocrypha.Games.BepInEx.Assets.ecosystem-schema.json";
    private const string NexusIdsResourceName = "Apocrypha.Games.BepInEx.Assets.bepinex-nexus-ids.json";

    /// <summary>Tracking methods the installers can honor (package-zip and subRoutes are not BepInEx things).</summary>
    private static readonly string[] SupportedTrackingMethods = ["subdir", "none", "state", "subdir-no-flatten"];

    [GeneratedRegex("/c/([^/]+)/", RegexOptions.CultureInvariant)]
    private static partial Regex CommunitySlugRegex();

    /// <summary>
    /// Loads the family's game rows from the embedded assets.
    /// </summary>
    /// <param name="excludedSettingsIdentifiers">
    /// Instances to skip — games that still have a hand-written module claiming their Steam
    /// app id (RoR2 until PR G). The Steam locator throws on duplicate app ids.
    /// </param>
    public static IReadOnlyList<BepInExGameData> LoadBundledGames(IReadOnlySet<string> excludedSettingsIdentifiers)
    {
        var assembly = typeof(EcosystemSchemaParser).Assembly;
        using var schemaStream = OpenResource(assembly, SchemaResourceName);
        using var nexusIdsStream = OpenResource(assembly, NexusIdsResourceName);
        return Parse(schemaStream, nexusIdsStream, excludedSettingsIdentifiers);
    }

    internal static IReadOnlyList<BepInExGameData> Parse(
        Stream schemaStream,
        Stream nexusIdsStream,
        IReadOnlySet<string> excludedSettingsIdentifiers)
    {
        var schema = JsonSerializer.Deserialize<EcosystemSchema>(schemaStream, JsonOptions)
                     ?? throw new InvalidOperationException("Ecosystem schema deserialized to null");
        var nexusIds = JsonSerializer.Deserialize<NexusIdMappingFile>(nexusIdsStream, JsonOptions)?.Mappings
                       ?? throw new InvalidOperationException("Nexus-id mapping deserialized to null");

        var results = new List<BepInExGameData>();
        var claimedSteamAppIds = new HashSet<uint>();
        var nexusLessDisplayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Deterministic order: schema map order isn't contractual, sort by the stable key.
        var instances = schema.Games.Values
            .SelectMany(game => game.R2modman ?? [])
            .OrderBy(instance => instance.SettingsIdentifier, StringComparer.Ordinal);

        foreach (var instance in instances)
        {
            if (instance.GameInstanceType != "game") continue;
            if (instance.PackageLoader != "bepinex") continue;
            if (instance.GameSelectionDisplayMode == "hidden") continue;
            if (excludedSettingsIdentifiers.Contains(instance.SettingsIdentifier)) continue;

            // A schema drift to rules we can't honor must fail the row, not misroute installs.
            if (!AreRulesSupported(instance.InstallRules)) continue;

            var steamAppIds = instance.Distributions
                .Where(distribution => distribution.Platform == "steam" && distribution.Identifier is not null)
                .Select(distribution => uint.TryParse(distribution.Identifier, out var appId) ? appId : 0u)
                .Where(appId => appId != 0 && !claimedSteamAppIds.Contains(appId))
                .ToImmutableArray();
            if (steamAppIds.IsEmpty) continue;

            if (!TryGetCommunitySlug(instance.PackageIndex, out var communitySlug)) continue;

            var primaryExe = instance.ExeNames.FirstOrDefault(exe => exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                             ?? instance.ExeNames.FirstOrDefault();
            if (primaryExe is null) continue;

            var nexusModsGameId = nexusIds.TryGetValue(instance.SettingsIdentifier, out var rawNexusId)
                ? (Optional<NexusModsGameId>)NexusModsGameId.From(rawNexusId)
                : Optional<NexusModsGameId>.None;

            // Nexus-less games resolve loadouts by display name (zero-sentinel path) —
            // names must be unique among them; first wins.
            if (!nexusModsGameId.HasValue && !nexusLessDisplayNames.Add(instance.Meta.DisplayName)) continue;

            foreach (var appId in steamAppIds) claimedSteamAppIds.Add(appId);

            results.Add(new BepInExGameData
            {
                SettingsIdentifier = instance.SettingsIdentifier,
                DisplayName = instance.Meta.DisplayName,
                GameId = GameId.From(instance.SettingsIdentifier),
                NexusModsGameId = nexusModsGameId,
                SteamAppIds = steamAppIds,
                PrimaryExeName = primaryExe,
                ExeNames = [..instance.ExeNames],
                CommunitySlug = communitySlug,
                CoverUrl = instance.Meta.IconUrl,
                CommunityIconUrl = schema.Communities.GetValueOrDefault(communitySlug)?.Meta?.Icon,
                DataFolderName = instance.DataFolderName,
                InstallRules = instance.InstallRules,
                RelativeFileExclusions = instance.RelativeFileExclusions,
            });
        }

        return results;
    }

    private static bool AreRulesSupported(List<EcosystemInstallRule> rules)
        => rules.All(rule => SupportedTrackingMethods.Contains(rule.TrackingMethod) && rule.SubRoutes.Count == 0);

    internal static bool TryGetCommunitySlug(string packageIndex, [NotNullWhen(true)] out string? slug)
    {
        var match = CommunitySlugRegex().Match(packageIndex);
        slug = match.Success ? match.Groups[1].Value : null;
        return match.Success;
    }

    private static Stream OpenResource(Assembly assembly, string name)
        => assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"Embedded resource `{name}` not found");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private class NexusIdMappingFile
    {
        [JsonPropertyName("mappings")]
        public Dictionary<string, uint> Mappings { get; init; } = new();
    }
}
