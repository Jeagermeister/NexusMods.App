using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.ModIo.DTOs;

/// <summary>
/// The mod.io paged envelope (<c>{ "data": [...], "result_count": ..., "result_total": ... }</c>).
/// </summary>
[PublicAPI]
public class PagedResultDto<T>
{
    [JsonPropertyName("data")] public required T[] Data { get; init; }
    [JsonPropertyName("result_count")] public int ResultCount { get; init; }
    [JsonPropertyName("result_total")] public int ResultTotal { get; init; }
}

/// <summary>
/// A game on mod.io (subset of the Game Object we consume).
/// </summary>
[PublicAPI]
public class GameDto
{
    [JsonPropertyName("id")] public required ulong Id { get; init; }
    [JsonPropertyName("name_id")] public required string NameId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("profile_url")] public string? ProfileUrl { get; init; }
}

/// <summary>
/// A mod on mod.io (subset of the Mod Object we consume). <see cref="Modfile"/> is the
/// mod's latest released file, embedded by the API — the common case needs no second call.
/// </summary>
[PublicAPI]
public class ModDto
{
    [JsonPropertyName("id")] public required ulong Id { get; init; }
    [JsonPropertyName("game_id")] public required ulong GameId { get; init; }
    [JsonPropertyName("name_id")] public required string NameId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("profile_url")] public string? ProfileUrl { get; init; }
    [JsonPropertyName("logo")] public LogoDto? Logo { get; init; }
    [JsonPropertyName("modfile")] public ModfileDto? Modfile { get; init; }
}

/// <summary>
/// A mod's logo images.
/// </summary>
[PublicAPI]
public class LogoDto
{
    [JsonPropertyName("thumb_320x180")] public string? Thumb320X180 { get; init; }
    [JsonPropertyName("original")] public string? Original { get; init; }
}

/// <summary>
/// A released file of a mod (subset of the Modfile Object we consume).
/// </summary>
[PublicAPI]
public class ModfileDto
{
    [JsonPropertyName("id")] public required ulong Id { get; init; }
    [JsonPropertyName("mod_id")] public required ulong ModId { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("filename")] public required string Filename { get; init; }
    [JsonPropertyName("filesize")] public ulong Filesize { get; init; }
    [JsonPropertyName("date_added")] public long DateAdded { get; init; }
    [JsonPropertyName("download")] public required DownloadDto Download { get; init; }
}

/// <summary>
/// A modfile's download descriptor. <see cref="BinaryUrl"/> is a time-limited URL
/// (see <see cref="DateExpires"/>) that 302s to the mod.io CDN.
/// </summary>
[PublicAPI]
public class DownloadDto
{
    [JsonPropertyName("binary_url")] public required string BinaryUrl { get; init; }
    [JsonPropertyName("date_expires")] public long DateExpires { get; init; }
}

/// <summary>
/// The mod.io error envelope (<c>{ "error": { "code": ..., "message": ... } }</c>).
/// </summary>
[PublicAPI]
public class ErrorEnvelopeDto
{
    [JsonPropertyName("error")] public ErrorDto? Error { get; init; }
}

/// <summary>
/// A mod.io API error.
/// </summary>
[PublicAPI]
public class ErrorDto
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("error_ref")] public int ErrorRef { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}
