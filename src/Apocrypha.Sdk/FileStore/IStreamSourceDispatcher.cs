using NexusMods.Hashing.xxHash3;

namespace Apocrypha.Sdk.FileStore;

public interface IStreamSourceDispatcher
{
    public ValueTask<Stream?> OpenAsync(Hash hash, CancellationToken cancellationToken = default);
    
    public bool Exists(Hash hash);
}
