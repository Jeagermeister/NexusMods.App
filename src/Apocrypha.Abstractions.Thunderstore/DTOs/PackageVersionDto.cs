using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Thunderstore.DTOs;

/// <summary>
/// A single published version of a Thunderstore package, as returned by
/// <c>GET /api/experimental/package/{namespace}/{name}/{version}/</c> (and embedded as
/// <c>latest</c> in <see cref="PackageDto"/>).
/// </summary>
[PublicAPI]
public class PackageVersionDto
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version_number")]
    public required string VersionNumber { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>
    /// Exact-version dependency strings in the <c>Namespace-Name-1.2.3</c> form.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; init; } = [];

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("downloads")]
    public long Downloads { get; init; }

    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("website_url")]
    public string? WebsiteUrl { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// The <see cref="PackageVersionRef"/> identifying this version.
    /// </summary>
    [JsonIgnore]
    public PackageVersionRef VersionRef => new(new PackageRef(Namespace, Name), VersionNumber);
}
