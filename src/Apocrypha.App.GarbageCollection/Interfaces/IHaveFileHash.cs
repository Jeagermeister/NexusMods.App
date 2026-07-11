using NexusMods.Hashing.xxHash3;

namespace Apocrypha.App.GarbageCollection.Interfaces;

/// <summary>
///     This is a marker interface for types that have a file hash.
/// </summary>
public interface IHaveFileHash
{
    /// <summary>
    ///     The type which has a file hash.
    /// </summary>
    public Hash Hash { get; }
}
