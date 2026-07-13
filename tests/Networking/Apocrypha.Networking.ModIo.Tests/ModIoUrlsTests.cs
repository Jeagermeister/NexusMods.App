using FluentAssertions;
using Apocrypha.Abstractions.ModIo;
using Xunit;

namespace Apocrypha.Networking.ModIo.Tests;

public class ModIoUrlsTests
{
    [Theory]
    [InlineData("https://mod.io/g/baldursgate3/m/some-mod", "baldursgate3", "some-mod")]
    [InlineData("https://www.mod.io/g/baldursgate3/m/some-mod", "baldursgate3", "some-mod")]
    [InlineData("https://mod.io/g/baldursgate3/m/some-mod/", "baldursgate3", "some-mod")]
    [InlineData("http://mod.io/g/readyornot/m/another_mod", "readyornot", "another_mod")]
    [InlineData("  https://mod.io/g/baldursgate3/m/some-mod  ", "baldursgate3", "some-mod")]
    [InlineData("https://mod.io/g/baldursgate3/m/some-mod#description", "baldursgate3", "some-mod")]
    [InlineData("https://mod.io/g/baldursgate3/m/some-mod?tab=files", "baldursgate3", "some-mod")]
    public void TryParseModUrl_AcceptsValidLinks(string input, string expectedGame, string expectedMod)
    {
        ModIoUrls.TryParseModUrl(input, out var game, out var mod).Should().BeTrue();
        game.Should().Be(expectedGame);
        mod.Should().Be(expectedMod);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://example.com/g/baldursgate3/m/some-mod")]
    [InlineData("https://evilmod.io/g/baldursgate3/m/some-mod")]
    [InlineData("https://mod.io/g/baldursgate3")]
    [InlineData("https://mod.io/g/baldursgate3/m")]
    [InlineData("https://mod.io/baldursgate3/some-mod")]
    [InlineData("ftp://mod.io/g/baldursgate3/m/some-mod")]
    [InlineData("https://thunderstore.io/c/riskofrain2/")]
    public void TryParseModUrl_RejectsInvalidLinks(string? input)
    {
        ModIoUrls.TryParseModUrl(input, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ApiUris_CarryApiKeyAndFilters()
    {
        ModIoUrls.GetGamesApiUri("KEY", nameId: "baldursgate3").ToString()
            .Should().Be("https://api.mod.io/v1/games?api_key=KEY&name_id=baldursgate3");

        ModIoUrls.GetModsApiUri("KEY", gameId: 3049, nameId: "some-mod").ToString()
            .Should().Be("https://api.mod.io/v1/games/3049/mods?api_key=KEY&name_id=some-mod");

        ModIoUrls.GetModApiUri("KEY", gameId: 3049, modId: 12345).ToString()
            .Should().Be("https://api.mod.io/v1/games/3049/mods/12345?api_key=KEY");
    }

    [Fact]
    public void SiteUris_AreWellFormed()
    {
        ModIoUrls.GetGamePageUri("baldursgate3").ToString().Should().Be("https://mod.io/g/baldursgate3");
        ModIoUrls.GetModPageUri("baldursgate3", "some-mod").ToString().Should().Be("https://mod.io/g/baldursgate3/m/some-mod");
    }
}
