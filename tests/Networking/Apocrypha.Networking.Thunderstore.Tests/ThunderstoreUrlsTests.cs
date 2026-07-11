using FluentAssertions;
using Apocrypha.Abstractions.Thunderstore;
using Xunit;

namespace Apocrypha.Networking.Thunderstore.Tests;

public class ThunderstoreUrlsTests
{
    [Theory]
    [InlineData("subnautica", "https://thunderstore.io/c/subnautica/")]
    [InlineData("riskofrain2", "https://thunderstore.io/c/riskofrain2/")]
    [InlineData("lethal-company", "https://thunderstore.io/c/lethal-company/")]
    public void GetCommunityUri_BuildsCommunityBrowsePage(string slug, string expected)
    {
        ThunderstoreUrls.GetCommunityUri(slug).Should().Be(new Uri(expected));
    }

    [Fact]
    public void GetPackagePageUri_BuildsGlobalPackagePage()
    {
        var package = new PackageRef("BepInEx", "BepInExPack");
        ThunderstoreUrls.GetPackagePageUri(package)
            .Should().Be(new Uri("https://thunderstore.io/package/BepInEx/BepInExPack/"));
    }

    [Fact]
    public void GetDownloadUri_BuildsVersionDownloadEndpoint()
    {
        var version = new PackageVersionRef(new PackageRef("BepInEx", "BepInExPack"), "5.4.2100");
        ThunderstoreUrls.GetDownloadUri(version)
            .Should().Be(new Uri("https://thunderstore.io/package/download/BepInEx/BepInExPack/5.4.2100/"));
    }
}
