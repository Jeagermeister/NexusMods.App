using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Steam.DTOs;
using Apocrypha.Abstractions.Steam.Values;
using NexusMods.Paths;
using SteamKit2;

namespace Apocrypha.Networking.Steam.Local;

/// <summary>
/// Reads Steam depot manifests from the local Steam <c>depotcache</c> directory (populated by the
/// Steam client whenever a game is installed or updated) and parses them into the app's
/// <see cref="Manifest"/> DTO.
/// </summary>
/// <remarks>
/// Linux fork: this is the foundation of login-free, download-free game version recognition.
/// The cached <c>&lt;depotId&gt;_&lt;manifestId&gt;.manifest</c> files hold the full vanilla file list
/// (path + SHA1 + size) with <b>plaintext</b> filenames, so an installed game's version can be
/// recognised without contacting Steam or re-downloading any game data. Verified against SteamKit2
/// 3.3.1: <see cref="DepotManifest.LoadFromFile"/> parses these files directly and reports
/// <c>FilenamesEncrypted == false</c>.
/// </remarks>
public class LocalManifestReader
{
    private readonly ILogger<LocalManifestReader> _logger;

    public LocalManifestReader(ILogger<LocalManifestReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The conventional name Steam uses for a cached depot manifest file.
    /// </summary>
    public static string ManifestFileName(DepotId depotId, ManifestId manifestId) => $"{depotId.Value}_{manifestId.Value}.manifest";

    /// <summary>
    /// Try to read and parse a cached depot manifest given only its manifest id, by locating the
    /// <c>&lt;depotId&gt;_&lt;manifestId&gt;.manifest</c> file in <paramref name="depotCacheDirectory"/> (manifest ids are
    /// globally unique, so the depot id is recovered from the file name). This is what the app uses at
    /// runtime, where the Steam locator exposes installed manifest ids but not their depot ids.
    /// Returns <c>null</c> when no matching manifest file is found or it cannot be parsed.
    /// </summary>
    public Manifest? TryReadManifestByManifestId(AbsolutePath depotCacheDirectory, ManifestId manifestId)
    {
        if (!TryFindManifestFile(depotCacheDirectory, manifestId, out _, out var depotId))
        {
            _logger.LogDebug("No cached Steam manifest found for manifest id {ManifestId} under {Path}", manifestId.Value, depotCacheDirectory);
            return null;
        }

        return TryReadManifest(depotCacheDirectory, depotId, manifestId);
    }

    /// <summary>
    /// Locate the cached <c>&lt;depotId&gt;_&lt;manifestId&gt;.manifest</c> file for a given manifest id within
    /// <paramref name="depotCacheDirectory"/>, recovering the depot id from the file name. Manifest ids are
    /// globally unique, so at most one file matches. Returns <c>false</c> when the directory is missing or no
    /// matching file exists. This is pure file-name resolution and does not parse the manifest contents.
    /// </summary>
    public static bool TryFindManifestFile(AbsolutePath depotCacheDirectory, ManifestId manifestId, out AbsolutePath manifestPath, out DepotId depotId)
    {
        manifestPath = default(AbsolutePath);
        depotId = default(DepotId);

        if (!depotCacheDirectory.DirectoryExists())
            return false;

        var suffix = $"_{manifestId.Value}.manifest";
        foreach (var file in depotCacheDirectory.EnumerateFiles("*.manifest", recursive: false))
        {
            var fileName = file.FileName.ToString();
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            var depotPart = fileName[..^suffix.Length];
            if (!uint.TryParse(depotPart, out var depotIdValue))
                continue;

            manifestPath = file;
            depotId = DepotId.From(depotIdValue);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to read and parse the cached depot manifest for the given depot/manifest pair from the
    /// provided <paramref name="depotCacheDirectory"/>. Returns <c>null</c> when the manifest file is
    /// absent, has encrypted filenames (i.e. was never decrypted locally), or fails to parse.
    /// </summary>
    public Manifest? TryReadManifest(AbsolutePath depotCacheDirectory, DepotId depotId, ManifestId manifestId)
    {
        var manifestPath = depotCacheDirectory / ManifestFileName(depotId, manifestId);
        if (!manifestPath.FileExists)
        {
            _logger.LogDebug("No cached Steam manifest at {Path}", manifestPath);
            return null;
        }

        try
        {
            var depotManifest = DepotManifest.LoadFromFile(manifestPath.ToNativeSeparators(OSInformation.Shared));
            if (depotManifest is null)
            {
                _logger.LogWarning("SteamKit2 could not load cached manifest {Path}", manifestPath);
                return null;
            }

            if (depotManifest.FilenamesEncrypted)
            {
                _logger.LogWarning("Cached Steam manifest {Path} has encrypted filenames and cannot be used without a depot key", manifestPath);
                return null;
            }

            return ManifestParser.Parse(depotManifest);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to parse cached Steam manifest {Path}", manifestPath);
            return null;
        }
    }
}
