using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// Maps a <see cref="GamePath"/> onto the directory casing that already exists on disk.
/// </summary>
/// <remarks>
/// <para>
/// Two mods can declare the same directory with different casing — <c>Data/F4SE/Plugins</c> and
/// <c>Data/F4SE/plugins</c> is a real case seen in the wild. <see cref="RelativePath"/> compares
/// case-insensitively, so the sync tree already treats those as one directory, but
/// <c>ToAbsolutePath</c> emits the literal casing carried by whichever <see cref="GamePath"/> it was
/// handed. On a case-sensitive filesystem that produces two real directories; the game opens only
/// the one it asked for and every file in the other is silently inert, with no error raised
/// anywhere. NTFS folds the two together, which is why this only bites on Linux.
/// </para>
/// <para>
/// Because the path model is already case-insensitive, rewriting the on-disk casing cannot desync
/// the disk state: a later scan that reports <c>Plugins/x.dll</c> still compares equal to a loadout
/// entry of <c>plugins/x.dll</c>.
/// </para>
/// </remarks>
public sealed class CaseCanonicalizer
{
    private readonly bool _enabled;
    private readonly GameLocations _locations;
    private readonly ILogger _logger;

    /// <summary>Parent directory -> case-insensitive lookup of the child directory names on disk.</summary>
    private readonly Dictionary<AbsolutePath, Dictionary<string, string>> _children = new();

    private readonly Dictionary<GamePath, AbsolutePath> _cache = new();

    private int _remapped;

    public CaseCanonicalizer(IOSInformation os, GameLocations locations, ILogger logger)
    {
        // On Windows and macOS the filesystem already folds case, so this is pure overhead.
        _enabled = os.IsUnix();
        _locations = locations;
        _logger = logger;
    }

    /// <summary>
    /// Number of paths whose casing was rewritten to match an existing directory. Zero on a clean
    /// install; non-zero means at least two mods disagreed about a directory's casing.
    /// </summary>
    public int RemappedCount => _remapped;

    public AbsolutePath Resolve(GamePath path)
    {
        if (!_enabled)
            return _locations.ToAbsolutePath(path);

        if (_cache.TryGetValue(path, out var cached))
            return cached;

        var root = _locations.ToAbsolutePath(new GamePath(path.LocationId, RelativePath.Empty));
        var resolved = ResolveUnder(root, path.Path.ToString());
        _cache[path] = resolved;
        return resolved;
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> beneath <paramref name="root"/>, substituting the
    /// casing already present on disk for each directory segment.
    /// </summary>
    public AbsolutePath ResolveUnder(AbsolutePath root, string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        // Only directory segments are canonicalised. The file name keeps the casing the loadout
        // declared — a file is opened by the game under a known name, whereas a directory is merely
        // a container that both mods intend to share.
        for (var i = 0; i < segments.Length - 1; i++)
            current /= ExistingDirectoryName(current, segments[i]);

        return segments.Length == 0 ? current : current / segments[^1];
    }

    private string ExistingDirectoryName(AbsolutePath parent, string wanted)
    {
        if (!_children.TryGetValue(parent, out var listing))
        {
            listing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (parent.DirectoryExists())
            {
                // Sorted so that a pre-existing collision resolves the same way on every apply
                // rather than following filesystem enumeration order.
                var names = parent.EnumerateDirectories("*", false)
                    .Select(static dir => dir.Name.ToString())
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray();

                foreach (var name in names)
                {
                    if (listing.TryGetValue(name, out var existing))
                    {
                        _logger.LogWarning(
                            "Directory {Parent} already contains both {Kept} and {Ignored}, which differ only in case. " +
                            "The game reads only one of them, so files in the other are inert. Deploying into the former; " +
                            "merge them on disk to fix",
                            parent, existing, name);
                        continue;
                    }

                    listing[name] = name;
                }
            }

            _children[parent] = listing;
        }

        if (listing.TryGetValue(wanted, out string? actual) && actual is not null)
        {
            if (!string.Equals(actual, wanted, StringComparison.Ordinal))
                _remapped++;
            return actual;
        }

        // Nothing on disk yet: the first mod to create this directory fixes its casing, and every
        // later mod in the same apply is folded into it.
        listing[wanted] = wanted;
        return wanted;
    }
}
