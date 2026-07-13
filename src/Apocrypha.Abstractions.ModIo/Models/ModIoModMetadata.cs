using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace Apocrypha.Abstractions.ModIo.Models;

/// <summary>
/// Represents a remote mod on mod.io (the all-files container, analogous to a Nexus Mods
/// mod page). Unlike Thunderstore packages, a mod.io mod belongs to exactly one game, so
/// game scoping is the single <see cref="GameNameId"/> slug (DESIGN-modio.md decision 4).
/// </summary>
[PublicAPI]
public partial class ModIoModMetadata : IModelDefinition
{
    private const string Namespace = "Apocrypha.ModIo.ModIoModMetadata";

    /// <summary>
    /// The mod.io numeric mod id — the true identity and natural lookup key.
    /// </summary>
    public static readonly UInt64Attribute ModId = new(Namespace, nameof(ModId)) { IsIndexed = true };

    /// <summary>
    /// The mod's URL slug (e.g. <c>bg3-native-camera</c>), used in <c>mod.io/g/{game}/m/{mod}</c> links.
    /// </summary>
    public static readonly StringAttribute NameId = new(Namespace, nameof(NameId));

    /// <summary>
    /// The owning game's URL slug (e.g. <c>baldursgate3</c>) — the game-scoping key matched
    /// against <see cref="IModIoGame.ModIoGameNameId"/>.
    /// </summary>
    public static readonly StringAttribute GameNameId = new(Namespace, nameof(GameNameId)) { IsIndexed = true };

    /// <summary>
    /// The owning game's mod.io numeric id.
    /// </summary>
    public static readonly UInt64Attribute GameId = new(Namespace, nameof(GameId));

    /// <summary>
    /// The mod's display name.
    /// </summary>
    public static readonly StringAttribute Name = new(Namespace, nameof(Name));

    /// <summary>
    /// The mod's page on mod.io.
    /// </summary>
    public static readonly UriAttribute ProfileUri = new(Namespace, nameof(ProfileUri));

    /// <summary>
    /// The mod's logo thumbnail on the mod.io CDN.
    /// </summary>
    public static readonly UriAttribute LogoUri = new(Namespace, nameof(LogoUri)) { IsOptional = true };

    /// <summary>
    /// Back-reference to all known files of this mod.
    /// </summary>
    public static readonly BackReferenceAttribute<ModIoFileMetadata> Files = new(ModIoFileMetadata.Mod);
}
