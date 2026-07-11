using NexusMods.MnemonicDB.Abstractions.Attributes;

namespace Apocrypha.Games.FOMOD;

public static class EmptyDirectory
{
    private const string Namespace = "NexusMods.Games.FOMOD.EmptyDirectory";
    
    public static readonly BooleanAttribute Directory = new(Namespace, nameof(EmptyDirectory));
}
