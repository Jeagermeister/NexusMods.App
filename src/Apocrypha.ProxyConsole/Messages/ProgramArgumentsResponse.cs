using MemoryPack;

namespace Apocrypha.ProxyConsole.Messages;

[MemoryPackable]
public partial class ProgramArgumentsResponse : IMessage
{
    public required string[] Arguments { get; init; }

}
