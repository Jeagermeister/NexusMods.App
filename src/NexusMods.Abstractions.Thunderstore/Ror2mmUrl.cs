using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NexusMods.Abstractions.Thunderstore;

/// <summary>
/// Parser for Thunderstore's one-click install protocol:
/// <c>ror2mm://v1/install/thunderstore.io/{namespace}/{name}/{version}/</c>.
/// The scheme is historical (Risk of Rain 2 Mod Manager) but is used by every
/// Thunderstore community's "Install with Mod Manager" button.
/// </summary>
[PublicAPI]
public static class Ror2mmUrl
{
    /// <summary>
    /// The URI scheme.
    /// </summary>
    public const string Scheme = "ror2mm";

    /// <summary>
    /// Parses a one-click install URL into a <see cref="PackageVersionRef"/>. Only version 1
    /// install URLs pointing at <c>thunderstore.io</c> (or a subdomain, e.g.
    /// <c>northstar.thunderstore.io</c> — r2modman accepts these too) are accepted.
    /// </summary>
    public static bool TryParseInstallUrl(string? url, [NotNullWhen(true)] out PackageVersionRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)) return false;

        // ror2mm://v1/install/{host}/{ns}/{name}/{version}/ parses as host "v1" +
        // path "install/{host}/{ns}/{name}/{version}".
        if (!uri.Host.Equals("v1", StringComparison.OrdinalIgnoreCase)) return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length != 5) return false;
        if (!segments[0].Equals("install", StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsThunderstoreHost(segments[1])) return false;

        var ns = Uri.UnescapeDataString(segments[2]);
        var name = Uri.UnescapeDataString(segments[3]);
        var version = Uri.UnescapeDataString(segments[4]);

        if (ns.Length == 0 || !PackageRef.IsValidName(name) || !PackageVersionRef.IsValidVersion(version)) return false;

        result = new PackageVersionRef(new PackageRef(ns, name), version);
        return true;
    }

    private static bool IsThunderstoreHost(string host)
        => host.Equals("thunderstore.io", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase);
}
