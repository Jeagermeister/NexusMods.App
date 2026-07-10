using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Thunderstore;
using NexusMods.Games.BepInEx.Installers;
using NexusMods.Games.BepInEx.Models;
using NexusMods.Games.BepInEx.Schema;
using NexusMods.Games.TestFramework;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace NexusMods.Games.BepInEx.Tests;

/// <summary>
/// The Phase 1 pilot's installer tests, ported verbatim from the deleted hand-written
/// RoR2 module (PR G, DESIGN-bepinex-family.md §9): the same r2modman routing conventions
/// (per-package plugins subfolder, shared config, metadata skipped), now exercised through
/// the schema-driven family row — RoR2 carries the canonical 5 rules.
/// </summary>
public class RiskOfRain2InstallerTests(ITestOutputHelper outputHelper)
    : ALibraryArchiveInstallerTests<RiskOfRain2InstallerTests, GenericBepInExGame>(outputHelper)
{
    private static readonly BepInExGameData RiskOfRain2Data =
        EcosystemSchemaParser.LoadBundledGames(new HashSet<string>())
            .Single(g => g.SettingsIdentifier == "RiskOfRain2");

    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddSingleton<GenericBepInExGame>(sp => new GenericBepInExGame(sp, RiskOfRain2Data))
            .AddSingleton<IGame>(sp => sp.GetRequiredService<GenericBepInExGame>())
            .AddSingleton<IGameData>(sp => sp.GetRequiredService<GenericBepInExGame>())
            .AddSingleton<BepInExPackInstaller>()
            .AddBepInExLoadoutItemModel()
            .AddBepInExPluginLoadoutItemModel()
            .AddThunderstoreModels()
            .AddUniversalGameLocator<GenericBepInExGame>(new Version("1.3.9"));
    }

    /// <summary>
    /// The migration contract: the schema row must be indistinguishable from the deleted
    /// hand-written module wherever identity is persisted (Brian's live loadout!) —
    /// GameId, display name, primary file, Steam id, and the Nexus-less zero-sentinel path.
    /// </summary>
    [Fact]
    public void FamilyRow_PreservesHandWrittenIdentity()
    {
        RiskOfRain2Data.GameId.Should().Be(GameId.From("RiskOfRain2"));
        RiskOfRain2Data.DisplayName.Should().Be("Risk of Rain 2");
        RiskOfRain2Data.PrimaryExeName.Should().Be("Risk of Rain 2.exe");
        RiskOfRain2Data.SteamAppIds.Should().Contain(632360u);
        RiskOfRain2Data.NexusModsGameId.HasValue.Should().BeFalse("RoR2 is Thunderstore-exclusive");
        RiskOfRain2Data.CommunitySlug.Should().Be("riskofrain2");
    }

    [Fact]
    public async Task PackInstaller_DeploysPackContentsToGameRoot_AndSkipsPackageMetadata()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(
            "manifest.json", "README.md", "icon.png",
            "BepInExPack/winhttp.dll",
            "BepInExPack/doorstop_config.ini",
            "BepInExPack/BepInEx/core/BepInEx.Preloader.dll");

        var group = await Install(typeof(BepInExPackInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).OrderBy(x => x).ToArray();

        targets.Should().BeEquivalentTo(
            "BepInEx/core/BepInEx.Preloader.dll",
            "doorstop_config.ini",
            "winhttp.dll");
    }

    [Fact]
    public async Task PluginInstaller_LooseFilesLandInPerPackagePluginsFolder()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths("manifest.json", "README.md", "icon.png", "CoolMod.dll");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).ToArray();

        targets.Should().ContainSingle()
            .Which.Should().Match(path => path.StartsWith("BepInEx/plugins/") && path.EndsWith("/CoolMod.dll"));
    }

    [Fact]
    public async Task PluginInstaller_RoutesCategoryFolders()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(
            "manifest.json",
            "plugins/Sub/Mod.dll",
            "patchers/Patch.dll",
            "config/settings.cfg");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, archive);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).OrderBy(x => x).ToArray();

        targets.Should().HaveCount(3);
        targets.Should().Contain("BepInEx/config/settings.cfg", "config files are shared and get no per-package subfolder");
        targets.Should().ContainSingle(x => x.StartsWith("BepInEx/plugins/") && x.EndsWith("/Sub/Mod.dll"));
        targets.Should().ContainSingle(x => x.StartsWith("BepInEx/patchers/") && x.EndsWith("/Patch.dll"));
    }

    [Fact]
    public async Task PluginInstaller_StripsSingleWrappingFolder_AndHandlesExplicitBepInExPrefix()
    {
        var loadout = await CreateLoadout();
        var wrapped = await AddFromPaths("MyModFolder/manifest.json", "MyModFolder/CoolMod.dll");

        var group = await Install(typeof(BepInExPluginInstaller), loadout, wrapped);
        var targets = ChildrenFilesAndHashes(group).Select(x => x.GamePath.Path.ToString()).ToArray();
        targets.Should().ContainSingle()
            .Which.Should().Match(path => path.StartsWith("BepInEx/plugins/") && path.EndsWith("/CoolMod.dll"));

        var prefixed = await AddFromPaths("manifest.json", "BepInEx/plugins/Thing/Mod.dll");
        var group2 = await Install(typeof(BepInExPluginInstaller), loadout, prefixed);
        var targets2 = ChildrenFilesAndHashes(group2).Select(x => x.GamePath.Path.ToString()).ToArray();
        targets2.Should().ContainSingle()
            .Which.Should().Match(path => path.StartsWith("BepInEx/plugins/") && path.EndsWith("/Thing/Mod.dll"));
    }

    [Fact]
    public async Task Installers_GateEachOthersArchives()
    {
        await CreateLoadout();
        var packArchive = await AddFromPaths("manifest.json", "BepInExPack/winhttp.dll", "BepInExPack/BepInEx/core/Core.dll");
        var pluginArchive = await AddFromPaths("manifest.json", "CoolMod.dll");

        var packInstaller = Game.LibraryItemInstallers.OfType<BepInExPackInstaller>().Single();
        var pluginInstaller = Game.LibraryItemInstallers.OfType<BepInExPluginInstaller>().Single();

        packInstaller.IsSupportedLibraryArchive(packArchive).Should().BeTrue();
        pluginInstaller.IsSupportedLibraryArchive(packArchive).Should().BeFalse("loader packs belong to the pack installer");
        packInstaller.IsSupportedLibraryArchive(pluginArchive).Should().BeFalse("plugin packages have no winhttp.dll");
        pluginInstaller.IsSupportedLibraryArchive(pluginArchive).Should().BeTrue();
    }
}
