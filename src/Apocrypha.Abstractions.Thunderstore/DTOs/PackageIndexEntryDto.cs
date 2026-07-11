using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Thunderstore.DTOs;

/// <summary>
/// One package in a community's v1 bulk package index
/// (<c>GET /c/{community}/api/v1/package/</c>). Deliberately slim: the index is megabytes of
/// JSON, so only the fields dependency resolution needs are bound.
/// </summary>
[PublicAPI]
public class PackageIndexEntryDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The package namespace. The v1 index calls this <c>owner</c>; it is the same value the
    /// experimental API calls <c>namespace</c>.
    /// </summary>
    [JsonPropertyName("owner")]
    public required string Owner { get; init; }

    [JsonPropertyName("is_deprecated")]
    public bool IsDeprecated { get; init; }

    /// <summary>
    /// All published versions, newest first (API contract).
    /// </summary>
    [JsonPropertyName("versions")]
    public PackageIndexVersionDto[] Versions { get; init; } = [];

    /// <summary>
    /// The <see cref="PackageRef"/> identifying this package.
    /// </summary>
    [JsonIgnore]
    public PackageRef PackageRef => new(Owner, Name);
}

/// <summary>
/// One version inside a <see cref="PackageIndexEntryDto"/>. The v1 index version objects carry
/// no namespace of their own — it comes from the entry's <c>owner</c>.
/// </summary>
[PublicAPI]
public class PackageIndexVersionDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version_number")]
    public required string VersionNumber { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; init; } = [];

    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Maps to the experimental-API version shape used everywhere downstream.
    /// </summary>
    public PackageVersionDto ToPackageVersionDto(string owner) => new()
    {
        Namespace = owner,
        Name = Name,
        VersionNumber = VersionNumber,
        FullName = $"{owner}-{Name}-{VersionNumber}",
        Description = Description,
        Icon = Icon,
        Dependencies = Dependencies,
        DateCreated = DateCreated,
        IsActive = IsActive,
    };
}
