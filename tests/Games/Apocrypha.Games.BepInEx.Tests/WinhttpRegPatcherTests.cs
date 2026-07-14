using FluentAssertions;
using Xunit;

namespace Apocrypha.Games.BepInEx.Tests;

/// <summary>
/// Table tests for the pure <c>user.reg</c> transform (CODE_REVIEW.md §7 #12: this rewrites a file
/// wineserver owns, so every branch is directed-tested). Covers the fd4 regression: an existing
/// conflicting <c>"winhttp"</c> value must be REPLACED, never duplicated.
/// </summary>
public class WinhttpRegPatcherTests
{
    private const long Ts = 1751932800;

    private const string RegNoSection = """
        WINE REGISTRY Version 2
        ;; All keys relative to \\User\\S-1-5-21

        [Software\\Wine\\Fonts] 1751932700
        "LogPixels"=dword:00000060
        """;

    private const string RegEmptySection = """
        WINE REGISTRY Version 2

        [Software\\Wine\\DllOverrides] 1751932700
        #time=1d8f2ab

        [Software\\Wine\\Fonts] 1751932700
        "LogPixels"=dword:00000060
        """;

    [Fact]
    public void NoSection_AppendsSectionWithOverride()
    {
        var result = WinhttpRegPatcher.Patch(RegNoSection, Ts);

        result.Should().NotBeNull();
        result.Should().Contain($"{WinhttpRegPatcher.SectionHeader} {Ts}");
        result.Should().Contain(WinhttpRegPatcher.OverrideValue);
        CountOccurrences(result!, "\"winhttp\"=").Should().Be(1);
    }

    [Fact]
    public void SectionWithoutWinhttp_InsertsAfterHeader()
    {
        var result = WinhttpRegPatcher.Patch(RegEmptySection, Ts);

        result.Should().NotBeNull();
        var headerLine = result!.IndexOf(WinhttpRegPatcher.SectionHeader, StringComparison.Ordinal);
        var overrideLine = result.IndexOf(WinhttpRegPatcher.OverrideValue, StringComparison.Ordinal);
        overrideLine.Should().BeGreaterThan(headerLine);
        CountOccurrences(result, "\"winhttp\"=").Should().Be(1);
        // Other sections untouched.
        result.Should().Contain("\"LogPixels\"=dword:00000060");
    }

    [Fact]
    public void OverrideAlreadyPresent_ReturnsNull()
    {
        var content = RegEmptySection.Replace("#time=1d8f2ab", $"#time=1d8f2ab\n{WinhttpRegPatcher.OverrideValue}");
        WinhttpRegPatcher.Patch(content, Ts).Should().BeNull();
    }

    [Fact]
    public void OverridePresentDifferentCase_ReturnsNull()
    {
        var content = RegEmptySection.Replace("#time=1d8f2ab", "#time=1d8f2ab\n\"WinHttp\"=\"NATIVE,builtin\"");
        WinhttpRegPatcher.Patch(content, Ts).Should().BeNull();
    }

    [Fact]
    public void ConflictingValue_IsReplacedNotDuplicated()
    {
        // fd4: a user's "winhttp"="disabled" previously got a duplicate key inserted ABOVE it,
        // which Wine resolved in the user's favor — silently disabling BepInEx.
        var content = RegEmptySection.Replace("#time=1d8f2ab", "#time=1d8f2ab\n\"winhttp\"=\"disabled\"");
        var result = WinhttpRegPatcher.Patch(content, Ts);

        result.Should().NotBeNull();
        result.Should().NotContain("\"winhttp\"=\"disabled\"");
        result.Should().Contain(WinhttpRegPatcher.OverrideValue);
        CountOccurrences(result!, "\"winhttp\"=").Should().Be(1);
    }

    [Fact]
    public void WinhttpKeyInOtherSection_IsIgnored()
    {
        // A "winhttp"= value in some OTHER section must not satisfy (or be clobbered by) the
        // DllOverrides check.
        var content = RegEmptySection + "\n[Software\\\\SomethingElse] 1\n\"winhttp\"=\"unrelated\"\n";
        var result = WinhttpRegPatcher.Patch(content, Ts);

        result.Should().NotBeNull();
        result.Should().Contain("\"winhttp\"=\"unrelated\"");
        result.Should().Contain(WinhttpRegPatcher.OverrideValue);
        CountOccurrences(result!, "\"winhttp\"=").Should().Be(2);
    }

    [Fact]
    public void CrlfContent_ConflictingValue_IsReplaced()
    {
        var content = RegEmptySection.Replace("#time=1d8f2ab", "#time=1d8f2ab\n\"winhttp\"=\"disabled\"")
            .Replace("\n", "\r\n");
        var result = WinhttpRegPatcher.Patch(content, Ts);

        result.Should().NotBeNull();
        result.Should().NotContain("\"winhttp\"=\"disabled\"");
        result.Should().Contain(WinhttpRegPatcher.OverrideValue);
    }

    [Fact]
    public void Patch_IsIdempotent()
    {
        var once = WinhttpRegPatcher.Patch(RegNoSection, Ts);
        once.Should().NotBeNull();
        WinhttpRegPatcher.Patch(once!, Ts).Should().BeNull();

        var viaSection = WinhttpRegPatcher.Patch(RegEmptySection, Ts);
        viaSection.Should().NotBeNull();
        WinhttpRegPatcher.Patch(viaSection!, Ts).Should().BeNull();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
