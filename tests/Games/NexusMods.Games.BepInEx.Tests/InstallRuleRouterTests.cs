using FluentAssertions;
using NexusMods.Games.BepInEx.Installers;
using NexusMods.Games.BepInEx.Schema;
using Xunit;

namespace NexusMods.Games.BepInEx.Tests;

/// <summary>
/// Rule-engine semantics (DESIGN-bepinex-family.md §6): canonical parity with the Phase 1
/// hand-written routing, plus the real deviant rule sets pulled from the vendored schema
/// (Subnautica state/QMods, GTFO GameData/Assets, ULTRAKILL "UMM Mods", H3VR Sideloader
/// extensions, Valheim SlimVML).
/// </summary>
public class InstallRuleRouterTests
{
    private const string Pkg = "Author-Package";

    private static readonly IReadOnlyDictionary<string, BepInExGameData> Games =
        EcosystemSchemaParser.LoadBundledGames(new HashSet<string> { "RiskOfRain2" })
            .ToDictionary(g => g.SettingsIdentifier);

    private static string Route(InstallRuleRouter router, string path)
        => router.Route(path.Split('/'), Pkg).ToString();

    private static InstallRuleRouter RouterFor(string settingsIdentifier)
        => new(Games[settingsIdentifier].InstallRules);

    // --- Canonical rules (empty list → fallback), Phase 1 parity ---

    [Theory]
    [InlineData("plugins/Foo.dll", "BepInEx/plugins/Author-Package/Foo.dll")]
    [InlineData("BepInEx/plugins/Foo.dll", "BepInEx/plugins/Author-Package/Foo.dll")]
    [InlineData("Plugins/sub/Foo.dll", "BepInEx/plugins/Author-Package/sub/Foo.dll")]
    [InlineData("config/pkg.cfg", "BepInEx/config/pkg.cfg")]
    [InlineData("BepInEx/config/pkg.cfg", "BepInEx/config/pkg.cfg")]
    [InlineData("patchers/Patch.dll", "BepInEx/patchers/Author-Package/Patch.dll")]
    [InlineData("core/Core.dll", "BepInEx/core/Author-Package/Core.dll")]
    [InlineData("monomod/Assembly.mm.dll", "BepInEx/monomod/Author-Package/Assembly.mm.dll")]
    [InlineData("Foo.dll", "BepInEx/plugins/Author-Package/Foo.dll")]
    [InlineData("assets/tex.png", "BepInEx/plugins/Author-Package/assets/tex.png")]
    [InlineData("BepInEx/weird/file.txt", "BepInEx/plugins/Author-Package/weird/file.txt")]
    public void CanonicalRules_RouteLikePhase1(string path, string expected)
    {
        var router = new InstallRuleRouter([]);
        Route(router, path).Should().Be(expected);
    }

    [Fact]
    public void CanonicalRules_LooseMonomodDllRoutesByLongestExtension()
    {
        // New over Phase 1: extension rules — .mm.dll beats .dll.
        var router = new InstallRuleRouter([]);
        Route(router, "Assembly.mm.dll").Should().Be("BepInEx/monomod/Author-Package/Assembly.mm.dll");
    }

    [Theory]
    [InlineData("plugins")]
    [InlineData("Config")]
    [InlineData("BepInEx")]
    [InlineData("monomod")]
    public void CanonicalRules_RouteSegmentsAreRecognized(string segment)
        => new InstallRuleRouter([]).IsRouteSegment(segment).Should().BeTrue();

    [Fact]
    public void CanonicalRules_OrdinaryWrapperIsNotARouteSegment()
        => new InstallRuleRouter([]).IsRouteSegment("MyModFolder").Should().BeFalse();

    // --- Subnautica: plugins/patchers/monomod are `state` (loose), QMods route ---

    [Theory]
    [InlineData("plugins/Foo.dll", "BepInEx/plugins/Foo.dll")]
    [InlineData("Foo.dll", "BepInEx/plugins/Foo.dll")]
    [InlineData("QMods/MyMod/mod.json", "QMods/MyMod/mod.json")]
    [InlineData("patchers/Patch.dll", "BepInEx/patchers/Patch.dll")]
    [InlineData("core/Core.dll", "BepInEx/core/Author-Package/Core.dll")]
    [InlineData("config/pkg.cfg", "BepInEx/config/pkg.cfg")]
    public void Subnautica_StateRulesDeployLoose(string path, string expected)
    {
        var router = RouterFor("Subnautica");
        Route(router, path).Should().Be(expected);
    }

    [Fact]
    public void Subnautica_QModsIsARouteSegment_SoItSurvivesWrapperStripping()
        => RouterFor("Subnautica").IsRouteSegment("QMods").Should().BeTrue();

    // --- GTFO: extra GameData (subdir) + Assets (state) routes ---

    [Theory]
    [InlineData("GameData/CustomThing.json", "BepInEx/GameData/Author-Package/CustomThing.json")]
    [InlineData("BepInEx/Assets/bundle.assets", "BepInEx/Assets/bundle.assets")]
    [InlineData("plugins/Foo.dll", "BepInEx/plugins/Author-Package/Foo.dll")]
    public void Gtfo_ExtraRoutesWork(string path, string expected)
    {
        var router = RouterFor("GTFO");
        Route(router, path).Should().Be(expected);
    }

    // --- ULTRAKILL: route with a space ---

    [Fact]
    public void Ultrakill_UmmModsRouteWithSpaceWorks()
    {
        var router = RouterFor("ULTRAKILL");
        Route(router, "UMM Mods/Mod/mod.dll").Should().Be("BepInEx/UMM Mods/Author-Package/Mod/mod.dll");
    }

    // --- H3VR: custom Sideloader extensions route loose files ---

    [Theory]
    [InlineData("Cool.hotmod", "BepInEx/Sideloader/Cool.hotmod")]
    [InlineData("Cool.h3mod", "BepInEx/Sideloader/Cool.h3mod")]
    [InlineData("Foo.dll", "BepInEx/plugins/Author-Package/Foo.dll")]
    public void H3vr_SideloaderExtensionsRouteLoose(string path, string expected)
    {
        var router = RouterFor("H3VR");
        Route(router, path).Should().Be(expected);
    }

    // --- Valheim: +SlimVML route ---

    [Fact]
    public void Valheim_SlimVmlRouteWorks()
    {
        var router = RouterFor("Valheim");
        Route(router, "SlimVML/Mod.dll").Should().Be("BepInEx/SlimVML/Author-Package/Mod.dll");
    }

    [Fact]
    public void AllFamilyGames_RulesCompileAndRouteADefaultFile()
    {
        foreach (var game in Games.Values)
        {
            var router = new InstallRuleRouter(game.InstallRules);
            var target = router.Route(["Anything.dll"], Pkg).ToString();
            target.Should().NotBeNullOrEmpty($"{game.SettingsIdentifier} must route loose files somewhere");
        }
    }
}
