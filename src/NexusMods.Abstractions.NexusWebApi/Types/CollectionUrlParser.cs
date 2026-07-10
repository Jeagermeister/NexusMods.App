using System.Diagnostics.CodeAnalysis;

namespace NexusMods.Abstractions.NexusWebApi.Types;

/// <summary>
/// Parses user-shared collection links into their components. Accepts nxm:// URLs and
/// nexusmods.com website URLs (both the "/games/{domain}/collections/{slug}" and the older
/// "/{domain}/collections/{slug}" shapes), with or without a "/revisions/{n}" suffix.
/// </summary>
public static class CollectionUrlParser
{
    /// <summary>
    /// Attempts to parse <paramref name="input"/> as a link to a collection.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out ParsedCollectionUrl? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("nxm" or "http" or "https"))
        {
            // links copied without a scheme ("www.nexusmods.com/...") still count
            if (!Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out uri)) return false;
        }

        // Uri normalizes the scheme and host to lowercase
        switch (uri.Scheme)
        {
            case "nxm":
            {
                var parts = SplitPath(uri);
                if (parts.Length == 0 || parts[0] != "collections") return false;
                return TryFromParts(uri.Host, parts, collectionsIndex: 0, out result);
            }
            case "http":
            case "https":
            {
                var host = uri.Host;
                if (host != "nexusmods.com" && !host.EndsWith(".nexusmods.com", StringComparison.Ordinal)) return false;

                var parts = SplitPath(uri);
                var collectionsIndex = Array.IndexOf(parts, "collections");

                // the segment before "collections" has to be a game domain
                if (collectionsIndex < 1) return false;
                var gameDomain = parts[collectionsIndex - 1];
                if (gameDomain == "games") return false;

                return TryFromParts(gameDomain, parts, collectionsIndex, out result);
            }
            default:
                return false;
        }
    }

    private static string[] SplitPath(Uri uri) => uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool TryFromParts(string gameDomain, string[] parts, int collectionsIndex, out ParsedCollectionUrl? result)
    {
        result = null;
        if (gameDomain.Length == 0) return false;

        var slugIndex = collectionsIndex + 1;
        if (slugIndex >= parts.Length) return false;
        var slug = parts[slugIndex];

        // trailing segments after the slug (website tabs like "/mods" or "/comments") are
        // ignored unless they point at a specific revision
        RevisionNumber? revision = null;
        if (parts.Length > slugIndex + 2 && parts[slugIndex + 1] == "revisions" && ulong.TryParse(parts[slugIndex + 2], out var revisionNumber))
        {
            revision = RevisionNumber.From(revisionNumber);
        }

        result = new ParsedCollectionUrl(gameDomain, CollectionSlug.From(slug), revision);
        return true;
    }
}

/// <summary>
/// Components of a link to a collection.
/// </summary>
/// <param name="GameDomain">Game domain as used on the Nexus Mods site, e.g. "stardewvalley".</param>
/// <param name="Slug">The collection slug.</param>
/// <param name="Revision">The revision number, if the link points at a specific revision.</param>
public sealed record ParsedCollectionUrl(string GameDomain, CollectionSlug Slug, RevisionNumber? Revision);
