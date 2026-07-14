namespace Apocrypha.Games.BepInEx;

/// <summary>
/// Pure string transform that ensures a Wine <c>user.reg</c> has the BepInEx
/// <c>"winhttp"="native,builtin"</c> DLL override. Extracted from the launch tool so the logic is
/// table-testable (CODE_REVIEW.md §7 #12) — this rewrites a file wineserver also owns, so every
/// branch needs directed tests.
///
/// Handles the case the old inline code got wrong (fd4): if the section already contains a
/// <c>"winhttp"</c> value with a DIFFERENT payload (e.g. a user's <c>"disabled"</c>), that line is
/// REPLACED rather than a duplicate key being inserted above it — Wine resolves duplicates in the
/// user's favor, which silently disabled BepInEx, and the duplicate itself is malformed registry.
/// </summary>
public static class WinhttpRegPatcher
{
    public const string OverrideValue = "\"winhttp\"=\"native,builtin\"";
    public const string SectionHeader = @"[Software\\Wine\\DllOverrides]";
    private const string WinhttpKey = "\"winhttp\"=";

    /// <summary>
    /// Returns the patched content, or <c>null</c> when the file already has the override and no
    /// write is needed.
    /// </summary>
    public static string? Patch(string content, long sectionTimestamp)
    {
        var headerIndex = content.IndexOf(SectionHeader, StringComparison.OrdinalIgnoreCase);
        if (headerIndex < 0)
        {
            // No DllOverrides section at all: append one with the override.
            return $"{content.TrimEnd('\n')}\n\n{SectionHeader} {sectionTimestamp}\n{OverrideValue}\n";
        }

        var headerLineEnd = content.IndexOf('\n', headerIndex);
        if (headerLineEnd < 0) headerLineEnd = content.Length - 1;

        // The section runs until the next '[' at the start of a line (or EOF).
        var sectionEnd = content.IndexOf("\n[", headerLineEnd, StringComparison.Ordinal);
        if (sectionEnd < 0) sectionEnd = content.Length;

        // Look for an existing "winhttp"= value inside this section only.
        var keyIndex = content.IndexOf(WinhttpKey, headerLineEnd, StringComparison.OrdinalIgnoreCase);
        if (keyIndex >= 0 && keyIndex < sectionEnd)
        {
            var lineEnd = content.IndexOf('\n', keyIndex);
            if (lineEnd < 0) lineEnd = content.Length;

            var existingLine = content[keyIndex..lineEnd].TrimEnd('\r');
            if (existingLine.Equals(OverrideValue, StringComparison.OrdinalIgnoreCase))
                return null; // Already correct; nothing to write.

            // Replace the conflicting value in place — never insert a duplicate key.
            return content[..keyIndex] + OverrideValue + content[lineEnd..];
        }

        // Section exists but has no winhttp value: insert right after the header line.
        return content.Insert(headerLineEnd + 1, OverrideValue + "\n");
    }
}
