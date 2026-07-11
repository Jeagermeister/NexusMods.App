
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Abstractions.NexusWebApi;

/// <summary>
/// Represents a job for logging in using OAuth.
/// </summary>
public interface IOAuthJob : IJobDefinition, IDisposable
{
    R3.BehaviorSubject<Uri?> LoginUriSubject { get; }
}
