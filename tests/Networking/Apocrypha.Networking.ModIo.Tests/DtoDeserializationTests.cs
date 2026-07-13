using System.Text.Json;
using FluentAssertions;
using Apocrypha.Abstractions.ModIo.DTOs;
using Xunit;

namespace Apocrypha.Networking.ModIo.Tests;

/// <summary>
/// Canned-JSON deserialization tests. Payload shapes match the mod.io v1 API docs
/// (docs.mod.io) — snake_case keys mapped via explicit JsonPropertyName attributes.
/// </summary>
public class DtoDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CanDeserializeGamesPage()
    {
        const string json = """
        {
            "data": [
                { "id": 3049, "name_id": "baldursgate3", "name": "Baldur's Gate 3", "profile_url": "https://mod.io/g/baldursgate3" }
            ],
            "result_count": 1,
            "result_offset": 0,
            "result_limit": 100,
            "result_total": 1
        }
        """;

        var page = JsonSerializer.Deserialize<PagedResultDto<GameDto>>(json, JsonOptions);

        page.Should().NotBeNull();
        page.ResultCount.Should().Be(1);
        page.ResultTotal.Should().Be(1);
        var game = page.Data.Should().ContainSingle().Subject;
        game.Id.Should().Be(3049u);
        game.NameId.Should().Be("baldursgate3");
        game.Name.Should().Be("Baldur's Gate 3");
    }

    [Fact]
    public void CanDeserializeModWithEmbeddedModfile()
    {
        const string json = """
        {
            "id": 2281296,
            "game_id": 3049,
            "status": 1,
            "name": "Some Mod",
            "name_id": "some-mod",
            "profile_url": "https://mod.io/g/baldursgate3/m/some-mod",
            "logo": {
                "filename": "logo.png",
                "original": "https://media.mod.io/original.png",
                "thumb_320x180": "https://media.mod.io/thumb320.png",
                "thumb_640x360": "https://media.mod.io/thumb640.png"
            },
            "modfile": {
                "id": 4567,
                "mod_id": 2281296,
                "date_added": 1700000000,
                "filesize": 1048576,
                "filename": "some-mod-1.2.pak.zip",
                "version": "1.2",
                "download": {
                    "binary_url": "https://api.mod.io/v1/games/3049/mods/2281296/files/4567/download/abcdef",
                    "date_expires": 1700086400
                }
            },
            "tags": [ { "name": "UI", "date_added": 1700000000 } ]
        }
        """;

        var mod = JsonSerializer.Deserialize<ModDto>(json, JsonOptions);

        mod.Should().NotBeNull();
        mod.Id.Should().Be(2281296u);
        mod.GameId.Should().Be(3049u);
        mod.NameId.Should().Be("some-mod");
        mod.Logo!.Thumb320X180.Should().Be("https://media.mod.io/thumb320.png");

        var file = mod.Modfile.Should().NotBeNull().And.Subject.As<ModfileDto>();
        file.Id.Should().Be(4567u);
        file.Version.Should().Be("1.2");
        file.Filename.Should().Be("some-mod-1.2.pak.zip");
        file.Filesize.Should().Be(1048576u);
        file.DateAdded.Should().Be(1700000000L);
        file.Download.BinaryUrl.Should().StartWith("https://api.mod.io/v1/games/3049/mods/2281296/files/4567/download");
        file.Download.DateExpires.Should().Be(1700086400L);
    }

    [Fact]
    public void CanDeserializeModWithoutModfile()
    {
        // mods with no released file have no modfile object
        const string json = """
        { "id": 1, "game_id": 3049, "name_id": "wip-mod", "name": "WIP Mod" }
        """;

        var mod = JsonSerializer.Deserialize<ModDto>(json, JsonOptions);

        mod.Should().NotBeNull();
        mod.Modfile.Should().BeNull();
        mod.Logo.Should().BeNull();
    }

    [Fact]
    public void CanDeserializeErrorEnvelope()
    {
        const string json = """
        { "error": { "code": 401, "error_ref": 11000, "message": "We cannot complete your request due to a malformed/missing api_key in your request." } }
        """;

        var envelope = JsonSerializer.Deserialize<ErrorEnvelopeDto>(json, JsonOptions);

        envelope.Should().NotBeNull();
        envelope.Error.Should().NotBeNull();
        envelope.Error.Code.Should().Be(401);
        envelope.Error.ErrorRef.Should().Be(11000);
        envelope.Error.Message.Should().Contain("api_key");
    }
}
