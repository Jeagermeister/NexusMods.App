using FluentAssertions;
using Apocrypha.Abstractions.Thunderstore;
using Xunit;

namespace Apocrypha.Networking.Thunderstore.Tests;

/// <summary>
/// Tests for the right-anchored parsing of Thunderstore identifiers: package names and versions
/// have restricted charsets (no dashes), namespaces may contain dashes, so parsing is anchored
/// on the last dash(es).
/// </summary>
public class PackageVersionRefTests
{
    [Theory]
    [InlineData("bbepis-BepInExPack-5.4.2100", "bbepis", "BepInExPack", "5.4.2100")]
    [InlineData("RiskofThunder-BepInEx_GUI-3.0.1", "RiskofThunder", "BepInEx_GUI", "3.0.1")]
    [InlineData("Risk-of-Thunder-SomeMod-1.2.3", "Risk-of-Thunder", "SomeMod", "1.2.3")]
    [InlineData("a-b-0.0.0", "a", "b", "0.0.0")]
    public void TryParse_ValidDependencyStrings(string input, string expectedNamespace, string expectedName, string expectedVersion)
    {
        var success = PackageVersionRef.TryParse(input, out var result);

        success.Should().BeTrue();
        result.Package.Namespace.Should().Be(expectedNamespace);
        result.Package.Name.Should().Be(expectedName);
        result.Version.Should().Be(expectedVersion);
        result.FullName.Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoVersionHere")]
    [InlineData("Namespace-Name")] // no version segment
    [InlineData("Namespace-Name-1.2")] // two-part version
    [InlineData("Namespace-Name-1.2.3.4")] // four-part version
    [InlineData("Namespace-Name-1.2.x")] // non-numeric version
    [InlineData("-Name-1.2.3")] // empty namespace
    [InlineData("Namespace--1.2.3")] // empty name
    [InlineData("Namespace-Na me-1.2.3")] // invalid name charset
    [InlineData("Namespace-Name-1.2.3-")] // trailing dash
    public void TryParse_InvalidDependencyStrings(string? input)
    {
        PackageVersionRef.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("bbepis-BepInExPack", "bbepis", "BepInExPack")]
    [InlineData("Risk-of-Thunder-RoR2BepInExPack", "Risk-of-Thunder", "RoR2BepInExPack")]
    public void PackageRef_TryParse_Valid(string input, string expectedNamespace, string expectedName)
    {
        var success = PackageRef.TryParse(input, out var result);

        success.Should().BeTrue();
        result.Namespace.Should().Be(expectedNamespace);
        result.Name.Should().Be(expectedName);
        result.FullName.Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoDash")]
    [InlineData("-Name")]
    [InlineData("Namespace-")]
    [InlineData("Namespace-Bad Name")]
    public void PackageRef_TryParse_Invalid(string? input)
    {
        PackageRef.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0", "9.0.0", 1)] // numeric, not lexicographic
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3", "1.10.0", -1)]
    public void CompareVersions_ComparesNumerically(string left, string right, int expectedSign)
    {
        Math.Sign(PackageVersionRef.CompareVersions(left, right)).Should().Be(expectedSign);
    }
}
