using System.Text;
using FluentAssertions;
using NexusMods.Games.BepInEx.Schema;
using Xunit;

namespace NexusMods.Games.BepInEx.Tests;

/// <summary>
/// Parser tests: the bundled-asset integration checks pin the facts the family relies on
/// (verification-set rows, uniqueness invariants), and the synthetic-schema unit tests pin
/// the filtering/dedup rules of DESIGN-bepinex-family.md §4.
/// </summary>
public class EcosystemSchemaParserTests
{
    private static readonly IReadOnlySet<string> ExcludeRoR2 = new HashSet<string> { "RiskOfRain2" };

    [Fact]
    public void LoadBundledGames_ProducesTheFamilyWithUniqueIdentities()
    {
        var games = EcosystemSchemaParser.LoadBundledGames(ExcludeRoR2);

        // ~211 BepInEx+Steam client instances in the vendored snapshot, minus exclusions/dedups.
        games.Should().HaveCountGreaterThan(150);

        games.Select(g => g.SettingsIdentifier).Should().OnlyHaveUniqueItems();
        games.Select(g => g.GameId).Should().OnlyHaveUniqueItems();
        games.SelectMany(g => g.SteamAppIds).Should().OnlyHaveUniqueItems(
            "the Steam locator builds a FrozenDictionary that throws on duplicate app ids");
        games.Where(g => !g.NexusModsGameId.HasValue).Select(g => g.DisplayName).Should().OnlyHaveUniqueItems(
            "Nexus-less games resolve loadouts by display name (zero-sentinel path)");

        games.Should().NotContain(g => g.SettingsIdentifier == "RiskOfRain2",
            "the hand-written RoR2 module still claims Steam app id 632360 until PR G");

        games.Should().AllSatisfy(game =>
        {
            game.CommunitySlug.Should().NotBeNullOrEmpty();
            game.PrimaryExeName.Should().NotBeNullOrEmpty();
            game.SteamAppIds.Should().NotBeEmpty();
            game.DisplayName.Should().NotBeNullOrEmpty();
        });

        games.Should().AllSatisfy(game => game.CoverUrl.Should().NotBeNullOrEmpty(
            "every instance in the snapshot carries a cover — tiles never fall back when online"));
        games.Count(game => game.CommunityIconUrl is not null).Should().BeGreaterThan(100,
            "most (not all) communities carry a 192×192 icon; legacy communities have no block");
    }

    [Fact]
    public void LoadBundledGames_VerificationSetRowsAreCorrect()
    {
        var games = EcosystemSchemaParser.LoadBundledGames(ExcludeRoR2).ToDictionary(g => g.SettingsIdentifier);

        var subnautica = games["Subnautica"];
        subnautica.DisplayName.Should().Be("Subnautica");
        subnautica.SteamAppIds.Should().Equal(264710u);
        subnautica.NexusModsGameId.HasValue.Should().BeTrue();
        subnautica.NexusModsGameId.Value.Value.Should().Be(1155u);
        subnautica.PrimaryExeName.Should().Be("Subnautica.exe");
        subnautica.CommunitySlug.Should().Be("subnautica");
        subnautica.InstallRules.Should().NotBeEmpty("Subnautica has deviant state/QMods rules the PR F engine consumes");
        subnautica.CoverUrl.Should().Be("subnautica/subnautica-cover-360x480.webp");
        subnautica.CommunityIconUrl.Should().BeNull("subnautica is a legacy community with no community block");

        var valheim = games["Valheim"];
        valheim.SteamAppIds.Should().Contain(892970u);
        valheim.NexusModsGameId.Value.Value.Should().Be(3667u);
        valheim.CommunitySlug.Should().Be("valheim");
        valheim.PrimaryExeName.Should().Be("valheim.exe", "the Windows binary is the primary file (Proton)");
        valheim.ExeNames.Should().Contain("valheim.x86_64", "the native-Linux marker feeds the future doorstop slice");

        var lethalCompany = games["LethalCompany"];
        lethalCompany.SteamAppIds.Should().Equal(1966720u);
        lethalCompany.NexusModsGameId.Value.Value.Should().Be(5848u);
        lethalCompany.CommunitySlug.Should().Be("lethal-company", "slugs come from packageIndex, never from display names");
        lethalCompany.CoverUrl.Should().Be("lethal-company/lethal-company-cover-360x480.webp");
        lethalCompany.CommunityIconUrl.Should().Be("lethal-company/lethal-company-icon-192x192.webp");

        games["H3VR"].NexusModsGameId.HasValue.Should().BeFalse("H3VR has no Nexus domain — zero-sentinel path");
    }

    [Fact]
    public void Parse_FiltersServersLoadersHiddenExcludedAndDupes()
    {
        var schema = SyntheticSchema(
            Instance("GameA", steam: "100", loader: "bepinex"),
            Instance("GameAServer", steam: "101", loader: "bepinex", instanceType: "server"),
            Instance("GameB", steam: "200", loader: "melonloader"),
            Instance("GameC", steam: "300", loader: "bepinex", displayMode: "hidden"),
            Instance("GameD", steam: "100", loader: "bepinex"),          // steam id already claimed by GameA
            Instance("GameE", steam: "", loader: "bepinex"),             // no steam id
            Instance("Excluded", steam: "400", loader: "bepinex"),
            Instance("GameF", steam: "500", loader: "bepinex", trackingMethod: "package-zip") // unsupported rules
        );

        var games = Parse(schema, mappings: "{}", excluded: ["Excluded"]);

        games.Select(g => g.SettingsIdentifier).Should().Equal("GameA");
    }

    [Fact]
    public void Parse_AppliesNexusMappingAndDedupsNexusLessDisplayNames()
    {
        var schema = SyntheticSchema(
            Instance("Mapped", steam: "100", loader: "bepinex", displayName: "Mapped Game"),
            Instance("NexusLess1", steam: "200", loader: "bepinex", displayName: "Same Name"),
            Instance("NexusLess2", steam: "300", loader: "bepinex", displayName: "Same Name")
        );

        var games = Parse(schema, mappings: """{"mappings": {"Mapped": 1234}}""", excluded: []);

        var mapped = games.Single(g => g.SettingsIdentifier == "Mapped");
        mapped.NexusModsGameId.HasValue.Should().BeTrue();
        mapped.NexusModsGameId.Value.Value.Should().Be(1234u);

        // First Nexus-less claimant of a display name wins; the second is dropped.
        games.Select(g => g.SettingsIdentifier).Should().BeEquivalentTo("Mapped", "NexusLess1");
    }

    [Fact]
    public void Parse_ResolvesCoverFromInstanceAndIconFromCommunity()
    {
        var schema = SyntheticSchemaWithCommunities(
            communities: """ "gamea": { "meta": { "icon": "gamea/gamea-icon-192x192.webp" } }, "gameb": {} """,
            Instance("GameA", steam: "100", loader: "bepinex", iconUrl: "gamea/gamea-cover-360x480.webp"),
            Instance("GameB", steam: "200", loader: "bepinex"),
            Instance("GameC", steam: "300", loader: "bepinex"));

        var games = Parse(schema, mappings: "{}", excluded: []).ToDictionary(g => g.SettingsIdentifier);

        games["GameA"].CoverUrl.Should().Be("gamea/gamea-cover-360x480.webp");
        games["GameA"].CommunityIconUrl.Should().Be("gamea/gamea-icon-192x192.webp");

        games["GameB"].CoverUrl.Should().BeNull();
        games["GameB"].CommunityIconUrl.Should().BeNull("the community block exists but carries no icon");

        games["GameC"].CommunityIconUrl.Should().BeNull("no community block at all (legacy communities)");
    }

    [Fact]
    public void Parse_PrefersWindowsExeAsPrimaryFile()
    {
        var schema = SyntheticSchema(
            Instance("Native", steam: "100", loader: "bepinex", exeNames: """["game.x86_64", "game.exe"]"""));

        var games = Parse(schema, mappings: "{}", excluded: []);

        games.Single().PrimaryExeName.Should().Be("game.exe");
        games.Single().ExeNames.Should().Equal("game.x86_64", "game.exe");
    }

    [Theory]
    [InlineData("https://thunderstore.io/c/lethal-company/api/v1/package-listing-index/", true, "lethal-company")]
    [InlineData("https://thunderstore.io/c/riskofrain2/api/v1/package-listing-index/", true, "riskofrain2")]
    [InlineData("https://thunderstore.io/api/v1/package-listing-index/", false, null)]
    public void TryGetCommunitySlug_DerivesFromPackageIndex(string packageIndex, bool expectSuccess, string? expectedSlug)
    {
        EcosystemSchemaParser.TryGetCommunitySlug(packageIndex, out var slug).Should().Be(expectSuccess);
        slug.Should().Be(expectedSlug);
    }

    private static IReadOnlyList<BepInExGameData> Parse(string schemaJson, string mappings, IEnumerable<string> excluded)
    {
        using var schemaStream = new MemoryStream(Encoding.UTF8.GetBytes(schemaJson));
        using var mappingStream = new MemoryStream(Encoding.UTF8.GetBytes(mappings));
        return EcosystemSchemaParser.Parse(schemaStream, mappingStream, excluded.ToHashSet());
    }

    private static string SyntheticSchema(params string[] instances) => SyntheticSchemaWithCommunities(communities: null, instances);

    private static string SyntheticSchemaWithCommunities(string? communities, params string[] instances)
    {
        // One synthetic game entry per instance keeps the fixture simple; the parser flattens anyway.
        var games = instances.Select((instance, i) => $"\"game-{i}\": {{ \"uuid\": \"u{i}\", \"label\": \"game-{i}\", \"meta\": {{ \"displayName\": \"Game {i}\" }}, \"r2modman\": [{instance}] }}");
        var communitiesJson = communities is null ? "" : $$""", "communities": { {{communities}} }""";
        return $$"""{ "schemaVersion": "0.3.0", "games": { {{string.Join(",", games)}} }{{communitiesJson}} }""";
    }

    private static string Instance(
        string settingsIdentifier,
        string steam,
        string loader,
        string instanceType = "game",
        string displayMode = "visible",
        string? displayName = null,
        string trackingMethod = "subdir",
        string? exeNames = null,
        string? iconUrl = null)
    {
        var distributions = steam.Length == 0 ? "[]" : $$"""[{ "platform": "steam", "identifier": "{{steam}}" }]""";
        var iconUrlJson = iconUrl is null ? "" : $$""", "iconUrl": "{{iconUrl}}" """;
        return $$"""
        {
            "meta": { "displayName": "{{displayName ?? settingsIdentifier}}"{{iconUrlJson}} },
            "distributions": {{distributions}},
            "settingsIdentifier": "{{settingsIdentifier}}",
            "packageIndex": "https://thunderstore.io/c/{{settingsIdentifier.ToLowerInvariant()}}/api/v1/package-listing-index/",
            "exeNames": {{exeNames ?? $"[\"{settingsIdentifier}.exe\"]"}},
            "gameInstanceType": "{{instanceType}}",
            "gameSelectionDisplayMode": "{{displayMode}}",
            "packageLoader": "{{loader}}",
            "installRules": [{ "route": "BepInEx/plugins", "defaultFileExtensions": [".dll"], "trackingMethod": "{{trackingMethod}}", "subRoutes": [], "isDefaultLocation": true }]
        }
        """;
    }
}
