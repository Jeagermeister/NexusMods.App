using Apocrypha.App.GarbageCollection.Errors;
using NexusMods.Hashing.xxHash3;

namespace Apocrypha.App.GarbageCollection.Utilities;

internal static class ThrowHelpers
{
    internal static void ThrowUnknownFileException(Hash hash) => throw new UnknownFileException(hash);
}
