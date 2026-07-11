using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Games.BepInEx.Installers;
using Apocrypha.Games.BepInEx.Models;
using Apocrypha.Games.BepInEx.Schema;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Apocrypha.Games.BepInEx.Tests;

/// <summary>
/// End-to-end installer tests through a REAL schema-driven family game (the Subnautica row,
/// whose deviant rules — plugins/patchers/monomod as <c>state</c>, a <c>QMods</c> route,
/// relativeFileExclusions — exercise the PR F rule engine beyond the canonical 5).
/// </summary>
public class SubnauticaInstallerTests(ITestOutputHelper outputHelper)
    : ALibraryArchiveInstallerTests<SubnauticaInstallerTests, GenericBepInExGame>(outputHelper)
{
    private static readonly BepInExGameData SubnauticaData =
        EcosystemSchemaParser.LoadBundledGames(new HashSet<string>())
            .Single(g => g.SettingsIdentifier == "Subnautica");

    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddSingleton<GenericBepInExGame>(sp => new GenericBepInExGame(sp, SubnauticaData))
            .AddSingleton<IGame>(sp => sp.GetRequiredService<GenericBepInExGame>())
            .AddSingleton<IGameData>(sp => sp.GetRequiredService<GenericBepInExGame>())
            .AddSingleton<BepInExPackInstaller>()
            .AddBepInExLoadoutItemModel()
            .AddBepInExPluginLoadoutItemModel()
            .AddThunderstoreModels()
            .AddUniversalGameLocator<GenericBepInExGame>(new Version("1.0.0"));
    }

    [Fact]
    public async Task QModsPackage_DeploysLooseUnderQMods_AndSkipsExclusions()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(
            "manifest.json", "icon.png", "README.md",
            "QMods/MyMod/mod.json",
            "QMods/MyMod/MyMod.dll");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).OrderBy(x => x).ToArray();

        // QMods is a `state` route: loose deploy, NO per-package subfolder; metadata is both
        // standard-skipped and in Subnautica's relativeFileExclusions.
        targets.Should().BeEquivalentTo(
            "QMods/MyMod/MyMod.dll",
            "QMods/MyMod/mod.json");
    }

    [Fact]
    public async Task LoosePluginDll_DeploysLooseIntoPlugins_NotIntoAPackageSubfolder()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths("manifest.json", "CoolMod.dll");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).ToArray();

        // Subnautica's plugins rule is `state` — r2modman parity is a loose file directly in
        // BepInEx/plugins (unlike canonical games, which get a per-package subfolder).
        targets.Should().ContainSingle().Which.Should().Be("BepInEx/plugins/CoolMod.dll");
    }

    [Fact]
    public async Task CoreStaysSubdir_WhileStateRoutesDeployLoose()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(
            "manifest.json",
            "core/Core.dll",
            "patchers/Patch.dll",
            "config/settings.cfg");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).OrderBy(x => x).ToArray();

        targets.Should().HaveCount(3);
        targets.Should().ContainSingle(x => x.StartsWith("BepInEx/core/") && x.EndsWith("/Core.dll"),
            "core keeps subdir tracking even on Subnautica");
        targets.Should().Contain("BepInEx/patchers/Patch.dll", "patchers is `state` on Subnautica — loose");
        targets.Should().Contain("BepInEx/config/settings.cfg");
    }

    [Fact]
    public async Task WrappedQModsPackage_KeepsTheQModsFolder()
    {
        var loadout = await CreateLoadout();
        // A single wrapping folder is stripped, but the QMods route folder itself must survive.
        var archive = await AddFromPaths(
            "Wrapper/manifest.json",
            "Wrapper/QMods/MyMod/MyMod.dll");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).ToArray();

        targets.Should().ContainSingle().Which.Should().Be("QMods/MyMod/MyMod.dll");
    }

    // --- Nexus-hosted archives (the collection install path): no Thunderstore manifest ---

    [Fact]
    public async Task NexusArchive_NoManifest_IsClaimedAndRoutedToPlugins()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(
            "CoolMod/CoolMod.dll",
            "CoolMod/assets/tex.png");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).OrderBy(x => x).ToArray();

        // Wrapper stripped, .dll extension-routes to plugins, the loose asset follows the
        // default rule — structure between the two is preserved.
        targets.Should().BeEquivalentTo(
            "BepInEx/plugins/CoolMod.dll",
            "BepInEx/plugins/assets/tex.png");
    }

    [Fact]
    public async Task NexusArchive_PackagedFromGameRoot_RoutesWithoutNesting()
    {
        var loadout = await CreateLoadout();
        // Nexus zips are often packaged from the game root; these must not double up
        // into BepInEx/plugins/BepInEx/….
        var archive = await AddFromPaths(
            "BepInEx/plugins/CoolMod/CoolMod.dll",
            "BepInEx/config/CoolMod.cfg");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).OrderBy(x => x).ToArray();

        targets.Should().BeEquivalentTo(
            "BepInEx/config/CoolMod.cfg",
            "BepInEx/plugins/CoolMod/CoolMod.dll");
    }

    [Fact]
    public void FallbackCollectionInstallDirectory_IsTheSchemaDefaultRoute()
    {
        // Collections install downloads no installer claims into this directory (Vortex
        // parity, upstream #2553) — when it's absent every unclaimed mod raises an
        // advanced-installer dialog instead.
        var fallback = Game.GetFallbackCollectionInstallDirectory(GameInstallation);

        fallback.HasValue.Should().BeTrue();
        fallback.Value.Should().Be(new GamePath(LocationId.Game, "BepInEx/plugins"));
    }
}
