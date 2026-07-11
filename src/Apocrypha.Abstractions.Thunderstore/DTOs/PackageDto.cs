using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Thunderstore.DTOs;

/// <summary>
/// A Thunderstore package (all-versions container), as returned by
/// <c>GET /api/experimental/package/{namespace}/{name}/</c>.
/// </summary>
[PublicAPI]
public class PackageDto
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("package_url")]
    public string? PackageUrl { get; init; }

    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    [JsonPropertyName("is_pinned")]
    public bool IsPinned { get; init; }

    [JsonPropertyName("is_deprecated")]
    public bool IsDeprecated { get; init; }

    [JsonPropertyName("latest")]
    public required PackageVersionDto Latest { get; init; }

    [JsonPropertyName("community_listings")]
    public CommunityListingDto[] CommunityListings { get; init; } = [];
}

/// <summary>
/// A package's listing in one Thunderstore community (game).
/// </summary>
[PublicAPI]
public class CommunityListingDto
{
    /// <summary>
    /// The community identifier/slug, e.g. <c>riskofrain2</c> or <c>lethal-company</c>.
    /// </summary>
    [JsonPropertyName("community")]
    public required string Community { get; init; }

    [JsonPropertyName("categories")]
    public string[] Categories { get; init; } = [];

    [JsonPropertyName("has_nsfw_content")]
    public bool HasNsfwContent { get; init; }

    [JsonPropertyName("review_status")]
    public string? ReviewStatus { get; init; }
}
