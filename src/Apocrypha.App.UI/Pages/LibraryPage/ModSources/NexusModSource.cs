using DynamicData.Kernel;
using JetBrains.Annotations;
using Apocrypha.Abstractions.ModSources;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Sdk.Games;

namespace Apocrypha.App.UI.Pages.LibraryPage.ModSources;

/// <summary>
/// <see cref="IModSource"/> adapter for Nexus Mods: a game supports it when it has a
/// <see cref="IGameData.NexusModsGameId"/>, and browsing goes to the game's Nexus page (resolved
/// through the domain/id mapping cache).
/// </summary>
/// <remarks>
/// Lives in App.UI for now, alongside the other adapters, because the browse URL needs the Nexus
/// domain mapping cache. Relocating the adapters into their source layers is a follow-up that
/// also resolves the App.UI → Networking.NexusWebApi layering inversion (CODE_REVIEW.md §5).
/// </remarks>
[UsedImplicitly]
internal sealed class NexusModSource : IModSource
{
    private readonly IGameDomainToGameIdMappingCache _gameIdMappingCache;

    public NexusModSource(IGameDomainToGameIdMappingCache gameIdMappingCache)
    {
        _gameIdMappingCache = gameIdMappingCache;
    }

    public ModSourceId Id => ModSourceId.NexusMods;
    public string DisplayName => "Nexus Mods";
    public bool IsEnabled => true;

    public bool SupportsGame(IGameData game) => game.NexusModsGameId.HasValue;

    public Optional<Uri> GetBrowseUri(IGameData game)
    {
        if (!game.NexusModsGameId.HasValue) return Optional<Uri>.None;

        try
        {
            var gameDomain = _gameIdMappingCache[game.NexusModsGameId.Value];
            return NexusModsUrlBuilder.GetGameUri(gameDomain);
        }
        catch
        {
            // A game id with no mapped domain can't be browsed; degrade to "no link" rather than
            // throwing out of a source-agnostic enumeration.
            return Optional<Uri>.None;
        }
    }
}
