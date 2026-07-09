using NexusMods.Abstractions.Steam;
using NexusMods.Paths;
using NexusMods.Sdk;

namespace NexusMods.Networking.Steam;


internal class AppDirectoryAuthStorage(IFileSystem fileSystem) : IAuthStorage
{
    private readonly AbsolutePath _storagePath = fileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory)
                                                 / ApplicationIdentity.DataDirectoryName
                                                 / "steam/auth";
    
    public async Task<(bool Success, byte[] Data)> TryLoad()
    {
        try
        {
            if (!_storagePath.FileExists)
                return (false, []);

            return (true, await _storagePath.ReadAllBytesAsync());
        }
        catch
        {
            return (false, []);
        }

    }

    public async Task SaveAsync(byte[] data)
    {
        if (!_storagePath.Parent.DirectoryExists())
            _storagePath.Parent.CreateDirectory();
        await _storagePath.WriteAllBytesAsync(data);
    }
}
