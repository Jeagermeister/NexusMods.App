using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Thunderstore;

/// <summary>
/// Identifies a Thunderstore package: the owning team's <see cref="Namespace"/> plus the package
/// <see cref="Name"/>. The pair is globally unique across all Thunderstore communities.
/// </summary>
[PublicAPI]
public readonly record struct PackageRef(string Namespace, string Name)
{
    /// <summary>
    /// The canonical <c>Namespace-Name</c> form used in Thunderstore URLs and dependency strings.
    /// </summary>
    public string FullName => $"{Namespace}-{Name}";

    /// <inheritdoc/>
    public override string ToString() => FullName;

    /// <summary>
    /// Parses a <c>Namespace-Name</c> string. Package names cannot contain <c>-</c> (Thunderstore
    /// restricts them to letters, digits, and underscores), so the name is the segment after the
    /// LAST dash and the namespace is everything before it (namespaces may themselves contain dashes).
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out PackageRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var lastDash = value.LastIndexOf('-');
        if (lastDash <= 0 || lastDash == value.Length - 1) return false;

        var ns = value[..lastDash];
        var name = value[(lastDash + 1)..];
        if (!IsValidName(name)) return false;

        result = new PackageRef(ns, name);
        return true;
    }

    /// <summary>
    /// True if the string is a valid Thunderstore package name (letters, digits, underscores).
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (name.Length == 0) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }
}

/// <summary>
/// Identifies an exact version of a Thunderstore package. This is the parsed form of a
/// Thunderstore dependency string (<c>Namespace-Name-1.2.3</c>).
/// </summary>
[PublicAPI]
public readonly record struct PackageVersionRef(PackageRef Package, string Version)
{
    /// <summary>
    /// The canonical <c>Namespace-Name-1.2.3</c> form (a Thunderstore dependency string).
    /// </summary>
    public string FullName => $"{Package.FullName}-{Version}";

    /// <inheritdoc/>
    public override string ToString() => FullName;

    /// <summary>
    /// Parses a Thunderstore dependency string (<c>Namespace-Name-1.2.3</c>). Parsing is anchored
    /// on the RIGHT: the version is the segment after the last dash (strict <c>major.minor.patch</c>),
    /// the name the segment before it, and the namespace everything else — namespaces may contain
    /// dashes, names and versions cannot.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out PackageVersionRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var lastDash = value.LastIndexOf('-');
        if (lastDash <= 0 || lastDash == value.Length - 1) return false;

        var version = value[(lastDash + 1)..];
        if (!IsValidVersion(version)) return false;

        if (!PackageRef.TryParse(value[..lastDash], out var packageRef)) return false;

        result = new PackageVersionRef(packageRef, version);
        return true;
    }

    /// <summary>
    /// True if the string is a strict Thunderstore <c>major.minor.patch</c> version number.
    /// </summary>
    public static bool IsValidVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 3) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0) return false;
            foreach (var c in part)
            {
                if (!char.IsAsciiDigit(c)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Compares two Thunderstore version numbers numerically (<c>10.0.0</c> &gt; <c>9.0.0</c>),
    /// falling back to ordinal string comparison if either is not strictly numeric.
    /// </summary>
    public static int CompareVersions(string left, string right)
    {
        if (System.Version.TryParse(left, out var leftVersion) && System.Version.TryParse(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);
        return string.CompareOrdinal(left, right);
    }
}
