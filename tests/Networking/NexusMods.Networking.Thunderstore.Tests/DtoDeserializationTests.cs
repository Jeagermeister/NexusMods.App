using System.Text.Json;
using FluentAssertions;
using NexusMods.Abstractions.Thunderstore.DTOs;
using Xunit;

namespace NexusMods.Networking.Thunderstore.Tests;

/// <summary>
/// Deserialization tests against captured live responses of the Thunderstore experimental API
/// (fetched 2026-07-08), so schema drift shows up as a test failure instead of a runtime surprise.
/// </summary>
public class DtoDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserializes_VersionResponse()
    {
        // GET https://thunderstore.io/api/experimental/package/bbepis/BepInExPack/5.4.2100/
        const string json =
            """
            {
                "namespace": "bbepis",
                "name": "BepInExPack",
                "version_number": "5.4.2100",
                "full_name": "bbepis-BepInExPack-5.4.2100",
                "description": "Unified BepInEx all-in-one modding pack - plugin framework, detour library",
                "icon": "https://gcdn.thunderstore.io/live/repository/icons/bbepis-BepInExPack-5.4.2100.png",
                "dependencies": [],
                "download_url": "https://thunderstore.io/package/download/bbepis/BepInExPack/5.4.2100/",
                "downloads": 9960,
                "date_created": "2022-07-31T16:52:58.225068Z",
                "website_url": "https://github.com/BepInEx/BepInEx",
                "is_active": true
            }
            """;

        var dto = JsonSerializer.Deserialize<PackageVersionDto>(json, JsonOptions);

        dto.Should().NotBeNull();
        dto!.Namespace.Should().Be("bbepis");
        dto.Name.Should().Be("BepInExPack");
        dto.VersionNumber.Should().Be("5.4.2100");
        dto.Dependencies.Should().BeEmpty();
        dto.IsActive.Should().BeTrue();
        dto.VersionRef.FullName.Should().Be("bbepis-BepInExPack-5.4.2100");
    }

    [Fact]
    public void Deserializes_PackageResponse_WithLatestAndListings()
    {
        // GET https://thunderstore.io/api/experimental/package/bbepis/BepInExPack/ (trimmed)
        const string json =
            """
            {
                "namespace": "bbepis",
                "name": "BepInExPack",
                "full_name": "bbepis-BepInExPack",
                "owner": "bbepis",
                "package_url": "https://thunderstore.io/package/bbepis/BepInExPack/",
                "date_created": "2019-04-10T09:12:29.866868Z",
                "date_updated": "2025-08-18T11:58:39.208724Z",
                "rating_score": -1,
                "is_pinned": true,
                "is_deprecated": false,
                "total_downloads": -1,
                "latest": {
                    "namespace": "bbepis",
                    "name": "BepInExPack",
                    "version_number": "5.4.2121",
                    "full_name": "bbepis-BepInExPack-5.4.2121",
                    "description": "Unified BepInEx all-in-one modding pack - plugin framework, detour library",
                    "icon": "https://gcdn.thunderstore.io/live/repository/icons/bbepis-BepInExPack-5.4.2121.png",
                    "dependencies": [
                        "RiskofThunder-BepInEx_GUI-3.0.1",
                        "RiskofThunder-RoR2BepInExPack-1.9.0"
                    ],
                    "download_url": "https://thunderstore.io/package/download/bbepis/BepInExPack/5.4.2121/",
                    "downloads": 1277697,
                    "date_created": "2025-08-18T11:58:38.241231Z",
                    "website_url": "https://github.com/risk-of-thunder/BepInEx",
                    "is_active": true
                },
                "community_listings": [
                    {
                        "has_nsfw_content": false,
                        "categories": ["Mods", "Libraries"],
                        "community": "riskofrain2",
                        "review_status": "approved"
                    }
                ]
            }
            """;

        var dto = JsonSerializer.Deserialize<PackageDto>(json, JsonOptions);

        dto.Should().NotBeNull();
        dto!.IsDeprecated.Should().BeFalse();
        dto.Latest.VersionRef.FullName.Should().Be("bbepis-BepInExPack-5.4.2121");
        dto.Latest.Dependencies.Should().HaveCount(2);
        dto.CommunityListings.Should().ContainSingle().Which.Community.Should().Be("riskofrain2");
    }
}
