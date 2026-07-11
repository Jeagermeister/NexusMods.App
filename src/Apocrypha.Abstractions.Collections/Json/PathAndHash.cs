using System.Text.Json.Serialization;
using Apocrypha.Abstractions.Collections.Types;
using NexusMods.Paths;
using Apocrypha.Sdk.Hashes;

// ReSharper disable InconsistentNaming

namespace Apocrypha.Abstractions.Collections.Json;

/// <summary>
/// Path and MD5 hash value as a pair.
/// </summary>
public class PathAndHash
{
    /// <summary>
    /// Relative path for the file's installation location.
    /// </summary>
    [JsonPropertyName("path")]
    public required RelativePath Path { get; init; }
    
    /// <summary>
    /// The MD5 hash of the file to put in that location.
    /// </summary>
    [JsonPropertyName("md5")]
    public required Md5Value MD5 { get; init; }
}
