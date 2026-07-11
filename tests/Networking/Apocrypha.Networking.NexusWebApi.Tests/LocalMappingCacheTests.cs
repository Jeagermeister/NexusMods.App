using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Networking.NexusWebApi.V1Interop;

namespace Apocrypha.Networking.NexusWebApi.Tests;

public class LocalMappingCacheTests
{
    [Fact]
    public void Test_Parse()
    {
        LocalMappingCache.TryParseJsonFile(NullLogger.Instance, out _, out _).Should().BeTrue();
    }
}
