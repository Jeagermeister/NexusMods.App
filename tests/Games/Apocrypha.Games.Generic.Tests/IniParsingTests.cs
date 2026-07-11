using FluentAssertions;
using IniParser;
using IniParser.Parser;
using Apocrypha.Games.Generic.FileAnalyzers;
using NexusMods.Paths;
using Xunit;

namespace Apocrypha.Games.Generic.Tests;

public class IniParsingTests
{
    private readonly IFileSystem _fs;

    public IniParsingTests(IFileSystem fs)
    {
        _fs = fs;
    }

    [Fact]
    public void ParseIniTest()
    {
        var assetsPath = _fs.GetKnownPath(KnownPath.EntryDirectory).Combine("Assets");
        var testIniPath = assetsPath.Combine("IniSamples/enblocal.ini");
        var config = IniAnalzyer.Config;
        var parser = new FileIniDataParser(new IniDataParser(config));
        var act = () => parser.ReadFile(testIniPath.ToString());
        act.Should().NotThrow();
    }
}
