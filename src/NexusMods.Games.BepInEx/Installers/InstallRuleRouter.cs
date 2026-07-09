using NexusMods.Games.BepInEx.Schema;
using NexusMods.Paths;

namespace NexusMods.Games.BepInEx.Installers;

/// <summary>
/// Routes a package entry to its deploy target according to the game's ecosystem-schema
/// <c>installRules</c> (DESIGN-bepinex-family.md §6). Pure and precomputed — one instance per
/// game, shared across installs.
/// </summary>
/// <remarks>
/// Tracking-method semantics, mapped onto this app's architecture: r2modman needs
/// <c>state</c> files because it has no per-file ownership tracking — our loadouts track
/// ownership natively, so <c>state</c> and <c>none</c> both simply mean "deploy loose into
/// the route, no per-package subfolder". <c>subdir</c>/<c>subdir-no-flatten</c> get a
/// per-package subfolder; archive-internal structure is preserved in both (BepInEx scans
/// plugin folders recursively, and preserving structure loses nothing — a deliberate,
/// simpler divergence from r2modman's subdir flattening).
/// </remarks>
public sealed class InstallRuleRouter
{
    /// <summary>
    /// The canonical 5 BepInEx rules — byte-identical for 204 of 214 BepInEx instances in
    /// the schema, and the fallback when a game carries no rules.
    /// </summary>
    public static readonly IReadOnlyList<EcosystemInstallRule> CanonicalRules =
    [
        new() { Route = "BepInEx/plugins", DefaultFileExtensions = [".dll"], TrackingMethod = "subdir", IsDefaultLocation = true },
        new() { Route = "BepInEx/core", DefaultFileExtensions = [], TrackingMethod = "subdir", IsDefaultLocation = false },
        new() { Route = "BepInEx/patchers", DefaultFileExtensions = [], TrackingMethod = "subdir", IsDefaultLocation = false },
        new() { Route = "BepInEx/monomod", DefaultFileExtensions = [".mm.dll"], TrackingMethod = "subdir", IsDefaultLocation = false },
        new() { Route = "BepInEx/config", DefaultFileExtensions = [], TrackingMethod = "none", IsDefaultLocation = false },
    ];

    private sealed record CompiledRule(
        string Route,
        string[] RouteSegments,
        string FinalSegment,
        string[] Extensions,
        bool UsesSubdir,
        bool IsDefault);

    private readonly CompiledRule[] _rules;
    private readonly CompiledRule _defaultRule;

    public InstallRuleRouter(IReadOnlyList<EcosystemInstallRule> rules)
    {
        var source = rules.Count > 0 ? rules : CanonicalRules;
        _rules = source
            .Select(rule => new CompiledRule(
                Route: rule.Route,
                RouteSegments: rule.Route.Split('/'),
                FinalSegment: rule.Route.Split('/')[^1],
                Extensions: rule.DefaultFileExtensions.ToArray(),
                UsesSubdir: rule.TrackingMethod is "subdir" or "subdir-no-flatten",
                IsDefault: rule.IsDefaultLocation))
            .ToArray();

        // The schema marks exactly one default per game; fall back to the first rule defensively.
        _defaultRule = _rules.FirstOrDefault(rule => rule.IsDefault) ?? _rules[0];
    }

    /// <summary>
    /// True when <paramref name="segment"/> names a route (either its final segment, its
    /// first segment, or "BepInEx") — used by the installer's wrapping-folder strip so a
    /// meaningful top-level folder (e.g. <c>QMods/</c>) is never treated as a wrapper.
    /// </summary>
    public bool IsRouteSegment(string segment)
    {
        if (segment.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) return true;
        return _rules.Any(rule =>
            segment.Equals(rule.FinalSegment, StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(rule.RouteSegments[0], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes the deploy target for one archive entry.
    /// </summary>
    /// <param name="parts">The entry's path segments, wrapping folder already stripped.</param>
    /// <param name="packageName">The canonical Namespace-Name package folder.</param>
    public RelativePath Route(string[] parts, string packageName)
    {
        // 1. Explicit route prefix — either the full route ("BepInEx/plugins/…", "QMods/…")
        //    or its bare final segment ("plugins/…"); longest match wins.
        CompiledRule? matched = null;
        var matchedLength = 0;
        foreach (var rule in _rules)
        {
            if (StartsWith(parts, rule.RouteSegments) && rule.RouteSegments.Length > matchedLength)
            {
                matched = rule;
                matchedLength = rule.RouteSegments.Length;
            }

            if (matchedLength == 0 &&
                parts.Length > 1 &&
                parts[0].Equals(rule.FinalSegment, StringComparison.OrdinalIgnoreCase))
            {
                matched = rule;
                matchedLength = 1;
            }
        }

        if (matched is not null)
            return Combine(matched, packageName, parts[matchedLength..]);

        // An explicit BepInEx/ prefix that matched no full route is noise — normalize it away
        // so the bare segments route as usual (Phase 1 parity).
        if (parts.Length > 1 && parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
            return Route(parts[1..], packageName);

        // 2. Extension routing — longest matching extension wins (.mm.dll beats .dll).
        var fileName = parts[^1];
        var byExtension = _rules
            .SelectMany(rule => rule.Extensions.Select(extension => (Rule: rule, Extension: extension)))
            .Where(x => fileName.EndsWith(x.Extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Extension.Length)
            .Select(x => x.Rule)
            .FirstOrDefault();
        if (byExtension is not null)
            return Combine(byExtension, packageName, parts);

        // 3. Everything else lands at the default location, path preserved.
        return Combine(_defaultRule, packageName, parts);
    }

    private static RelativePath Combine(CompiledRule rule, string packageName, string[] remainder)
    {
        var tail = string.Join('/', remainder);
        var target = rule.UsesSubdir
            ? $"{rule.Route}/{packageName}/{tail}"
            : $"{rule.Route}/{tail}";
        return RelativePath.FromUnsanitizedInput(target);
    }

    private static bool StartsWith(string[] parts, string[] prefix)
    {
        if (parts.Length <= prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!parts[i].Equals(prefix[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
