using FluentAssertions;
using NexusMods.Abstractions.Thunderstore;
using Xunit;

namespace NexusMods.Networking.Thunderstore.Tests;

/// <summary>
/// Tests for the ror2mm:// one-click install URL parser. The URL shape is
/// <c>ror2mm://v1/install/thunderstore.io/{namespace}/{name}/{version}/</c> as emitted by the
/// "Install with Mod Manager" button on thunderstore.io package pages.
/// </summary>
public class Ror2mmUrlTests
{
    [Theory]
    [InlineData("ror2mm://v1/install/thunderstore.io/BepInEx/BepInExPack/5.4.2100/", "BepInEx", "BepInExPack", "5.4.2100")]
    [InlineData("ror2mm://v1/install/thunderstore.io/BepInEx/BepInExPack/5.4.2100", "BepInEx", "BepInExPack", "5.4.2100")] // no trailing slash
    [InlineData("ror2mm://v1/install/northstar.thunderstore.io/Team/Some_Mod/1.0.0/", "Team", "Some_Mod", "1.0.0")] // subdomain host
    [InlineData("ROR2MM://V1/INSTALL/thunderstore.io/Team/Mod/1.2.3/", "Team", "Mod", "1.2.3")] // scheme/host case-insensitive
    public void TryParseInstallUrl_Valid(string url, string expectedNamespace, string expectedName, string expectedVersion)
    {
        var success = Ror2mmUrl.TryParseInstallUrl(url, out var result);

        success.Should().BeTrue();
        result.Package.Namespace.Should().Be(expectedNamespace);
        result.Package.Name.Should().Be(expectedName);
        result.Version.Should().Be(expectedVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("nxm://riskofrain2/mods/1/files/2")] // wrong scheme
    [InlineData("ror2mm://v2/install/thunderstore.io/Team/Mod/1.2.3/")] // unknown protocol version
    [InlineData("ror2mm://v1/uninstall/thunderstore.io/Team/Mod/1.2.3/")] // unknown action
    [InlineData("ror2mm://v1/install/evil.example.com/Team/Mod/1.2.3/")] // non-Thunderstore host
    [InlineData("ror2mm://v1/install/notthunderstore.io/Team/Mod/1.2.3/")] // suffix near-miss
    [InlineData("ror2mm://v1/install/thunderstore.io/Team/Mod/")] // missing version
    [InlineData("ror2mm://v1/install/thunderstore.io/Team/Mod/1.2/")] // two-part version
    [InlineData("ror2mm://v1/install/thunderstore.io/Team/Bad Name/1.2.3/")] // invalid name charset
    [InlineData("ror2mm://v1/install/thunderstore.io/Team/Mod/1.2.3/extra/")] // too many segments
    public void TryParseInstallUrl_Invalid(string? url)
    {
        Ror2mmUrl.TryParseInstallUrl(url, out _).Should().BeFalse();
    }
}
