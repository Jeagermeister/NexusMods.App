using FluentAssertions;
using Apocrypha.Games.CreationEngine.Emitters;

namespace Apocrypha.Games.CreationEngine.Tests.Fallout4;

/// <summary>
/// Pure-function coverage for the archive-limit diagnostic. No game install, no network.
/// </summary>
public class EngineLimitsEmitterTests
{
    [Fact]
    public void CountsOnlyArchivesOwnedByEnabledPlugins()
    {
        string[] archives =
        [
            "SomeMod - Main.ba2",
            "SomeMod - Textures.ba2",
            "somemod - Voices_en.ba2",   // case difference must still match
            "OrphanMod - Main.ba2",      // no matching plugin -> not loaded by the engine
            "BareArchive.ba2",           // no " - " separator: stem is the whole name
        ];
        string[] plugins = ["SomeMod", "BareArchive", "UnrelatedPlugin"];

        EngineLimitsEmitter.CountArchivesOwnedByPlugins(archives, plugins).Should().Be(4);
    }

    [Fact]
    public void ArchiveNameContainingSeparatorInModName_UsesFirstSeparator()
    {
        // "A - B - Main.ba2" splits at the FIRST " - ": owner stem is "A", not "A - B".
        string[] archives = ["A - B - Main.ba2"];

        EngineLimitsEmitter.CountArchivesOwnedByPlugins(archives, ["A"]).Should().Be(1);
        EngineLimitsEmitter.CountArchivesOwnedByPlugins(archives, ["A - B"]).Should().Be(0);
    }

    [Fact]
    public void ParsesTheRealBuffoutConfigShape()
    {
        // Mirrors the deployed file that motivated this diagnostic, comments and all.
        const string config = """
            [Patches]
            Achievements = false                # Enables achievements on modded saves
            ArchiveLimit = true                 # Effectively bypasses the limit
            MaxStdIO = 2048                    # Replaces the maximum stdio handles (msvcr110 hard cap)
            MemoryManager = true
            """;

        var mitigation = EngineLimitsEmitter.ParseBuffoutConfig(config);

        mitigation.ConfigFound.Should().BeTrue();
        mitigation.ArchiveLimitEnabled.Should().BeTrue();
        mitigation.MaxStdIO.Should().Be(2048);
        mitigation.IsFullyMitigated.Should().BeTrue();
    }

    [Theory]
    // The mod's shipped defaults: nothing mitigated.
    [InlineData("ArchiveLimit = false\nMaxStdIO = -1", false)]
    // Archive limit bypassed but handles still capped at 512: the main-menu crash remains.
    [InlineData("ArchiveLimit = true\nMaxStdIO = -1", false)]
    // Handles raised but archive ceiling still on.
    [InlineData("ArchiveLimit = false\nMaxStdIO = 2048", false)]
    // Both set: safe.
    [InlineData("ArchiveLimit = true\nMaxStdIO = 2048", true)]
    // Keys missing entirely (stripped-down config).
    [InlineData("MemoryManager = true", false)]
    public void MitigationRequiresBothSettings(string config, bool expected)
    {
        EngineLimitsEmitter.ParseBuffoutConfig(config).IsFullyMitigated.Should().Be(expected);
    }

    [Fact]
    public void DescribeNamesTheGap()
    {
        var notInstalled = new EngineLimitsEmitter.BuffoutMitigation(ConfigFound: false, ArchiveLimitEnabled: false, MaxStdIO: 0);
        notInstalled.Describe().Should().Contain("not installed");

        var halfConfigured = EngineLimitsEmitter.ParseBuffoutConfig("ArchiveLimit = true\nMaxStdIO = -1");
        halfConfigured.Describe().Should().Contain("ArchiveLimit = true").And.Contain("MaxStdIO = -1");
    }
}
