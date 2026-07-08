using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Paths;
using SteamKit2;

namespace NexusMods.Networking.Steam.Local;

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
