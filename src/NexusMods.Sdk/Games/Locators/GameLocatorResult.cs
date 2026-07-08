using System.Collections.Immutable;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NexusMods.Paths;

namespace NexusMods.Sdk.Games;

[PublicAPI]
public class GameLocatorResult
{
    /// <summary>
    /// The game that was located.
    /// </summary>
    public required IGameData Game { get; init; }

    /// <summary>
    /// The path of the game.
    /// </summary>
    public required AbsolutePath Path { get; init; }

    /// <summary>
    /// The store used to install the game.
    /// </summary>
    public required GameStore Store { get; init; }

    /// <summary>
    /// The opaque store identifier for the game.
    /// </summary>
    public required string StoreIdentifier { get; init; }

    /// <summary>
    /// The locator identifiers for the installation.
    /// </summary>
    public required ImmutableArray<LocatorId> LocatorIds { get; init; }

    /// <summary>
    /// The locator that found this instance.
    /// </summary>
    public required IGameLocator Locator { get; init; }

    /// <summary>
    /// The platform of the game.
    /// </summary>
    /// <remarks>
    /// On Linux you can install Windows versions using WINE/Proton in addition to native Linux builds.
    /// </remarks>
    public OSPlatform Platform { get; init; } = OSInformation.Shared.Platform;

    /// <summary>
    /// Target operating system.
    /// </summary>
    public IOSInformation TargetOS => new OSInformation(Platform);

    /// <summary>
    /// Optional linux compatability data provider.
    /// </summary>
    public ILinuxCompatabilityDataProvider? LinuxCompatabilityDataProvider { get; init; }

    /// <summary>
    /// For Steam installations, the path to the Steam client's global <c>depotcache</c> directory
    /// (which holds the on-disk <c>&lt;depotId&gt;_&lt;manifestId&gt;.manifest</c> files). Null for other stores.
    /// </summary>
    /// <remarks>
    /// Linux fork: used by login-free local version recognition to read Steam depot manifests without
    /// contacting Steam. The depotcache is global to the Steam install (not per-library), so it cannot
    /// be derived from the game's install path and is captured here by the Steam locator.
    /// </remarks>
    public AbsolutePath? SteamDepotCachePath { get; init; }
}
