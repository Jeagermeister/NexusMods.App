using FluentAssertions;
using Apocrypha.Abstractions.NexusWebApi.Types;

namespace Apocrypha.Networking.NexusWebApi.Tests.Types;

public class CollectionUrlParserTests
{
    [Theory]
    // website URL shape the app's URL builder produces
    [InlineData("https://www.nexusmods.com/games/stardewvalley/collections/tckf0m", "stardewvalley", "tckf0m", null)]
    [InlineData("https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/revisions/5", "stardewvalley", "tckf0m", 5ul)]
    // no www, with query string
    [InlineData("https://nexusmods.com/games/stardewvalley/collections/tckf0m?tab=mods", "stardewvalley", "tckf0m", null)]
    // older website shape without the "games" prefix
    [InlineData("https://next.nexusmods.com/stardewvalley/collections/tckf0m", "stardewvalley", "tckf0m", null)]
    [InlineData("https://www.nexusmods.com/stardewvalley/collections/tckf0m/revisions/12", "stardewvalley", "tckf0m", 12ul)]
    // website tab suffixes are ignored
    [InlineData("https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/mods", "stardewvalley", "tckf0m", null)]
    // nxm URLs, with and without revision
    [InlineData("nxm://stardewvalley/collections/tckf0m/revisions/5", "stardewvalley", "tckf0m", 5ul)]
    [InlineData("nxm://stardewvalley/collections/tckf0m", "stardewvalley", "tckf0m", null)]
    // surrounding whitespace from copy-paste
    [InlineData("  https://www.nexusmods.com/games/stardewvalley/collections/tckf0m \n", "stardewvalley", "tckf0m", null)]
    // scheme-less links, e.g. hand-typed or copied without the protocol
    [InlineData("www.nexusmods.com/games/stardewvalley/collections/tckf0m", "stardewvalley", "tckf0m", null)]
    [InlineData("next.nexusmods.com/stardewvalley/collections/tckf0m/revisions/3", "stardewvalley", "tckf0m", 3ul)]
    public void CanParseValidCollectionLinks(string input, string expectedDomain, string expectedSlug, ulong? expectedRevision)
    {
        var success = CollectionUrlParser.TryParse(input, out var result);

        success.Should().BeTrue();
        result!.GameDomain.Should().Be(expectedDomain);
        result.Slug.Value.Should().Be(expectedSlug);

        if (expectedRevision is null) result.Revision.Should().BeNull();
        else result.Revision!.Value.Value.Should().Be(expectedRevision.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    // wrong host
    [InlineData("https://example.com/games/stardewvalley/collections/tckf0m")]
    [InlineData("https://nexusmods.com.evil.example/games/stardewvalley/collections/tckf0m")]
    // mod links are not collection links
    [InlineData("nxm://stardewvalley/mods/123/files/456")]
    [InlineData("https://www.nexusmods.com/stardewvalley/mods/123")]
    // no game domain before "collections"
    [InlineData("https://www.nexusmods.com/collections/tckf0m")]
    [InlineData("https://www.nexusmods.com/games/collections/tckf0m")]
    // missing slug
    [InlineData("https://www.nexusmods.com/games/stardewvalley/collections")]
    [InlineData("nxm://stardewvalley/collections")]
    public void RejectsInvalidCollectionLinks(string? input)
    {
        var success = CollectionUrlParser.TryParse(input, out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }
}
