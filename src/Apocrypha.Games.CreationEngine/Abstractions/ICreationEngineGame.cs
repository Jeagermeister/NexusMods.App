using Mutagen.Bethesda.Plugins.Records;
using Apocrypha.Abstractions.Games;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;


using Apocrypha.Sdk.Games;

namespace Apocrypha.Games.CreationEngine.Abstractions;

/// <summary>
/// A common interface for all games that use the creation engine.
/// </summary>
public interface ICreationEngineGame : IGame
{
    public GamePath PluginsFile { get; }

    /// <summary>
    /// The engine's DLC manifest, or <c>null</c> for games that do not have one.
    /// </summary>
    /// <remarks>
    /// Only meaningful together with <see cref="KnownDlc"/>. Games that leave this null keep the
    /// previous behaviour of not touching the file at all.
    /// </remarks>
    public GamePath? DlcListFile => null;

    /// <summary>
    /// The official DLC plugins for this game, in the engine's release order.
    /// </summary>
    public IReadOnlyList<RelativePath> KnownDlc => [];
    /// <summary>
    /// Parse a plugin file. Currently, this loads only the header of the file, in the future
    /// we can add flags to load specific groups
    /// </summary>
    public ValueTask<IMod?> ParsePlugin(Hash hash, RelativePath? name = null);
}
