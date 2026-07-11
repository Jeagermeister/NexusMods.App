using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Abstractions.Library.Jobs;

/// <summary>
/// A job that adds a local file to the library
/// </summary>
public interface IAddLocalFile : IJobDefinition<LocalFile.ReadOnly>
{
    /// <summary>
    /// The source file path
    /// </summary>
    public AbsolutePath FilePath { get; }
}
