using DynamicData.Kernel;
using JetBrains.Annotations;
using Apocrypha.Abstractions.ModSources;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Sdk.Games;

namespace Apocrypha.App.UI.Pages.LibraryPage.ModSources;

/// <summary>
/// <see cref="IModSource"/> adapter for Thunderstore: a game supports it when it declares an
/// <see cref="IThunderstoreCommunityGame"/> community, and browsing goes to that community.
/// </summary>
[UsedImplicitly]
internal sealed class ThunderstoreModSource : IModSource
{
    public ModSourceId Id => ModSourceId.Thunderstore;
    public string DisplayName => "Thunderstore";
    public bool IsEnabled => true;

    public bool SupportsGame(IGameData game) => game is IThunderstoreCommunityGame;

    public Optional<Uri> GetBrowseUri(IGameData game)
    {
        if (game is not IThunderstoreCommunityGame thunderstoreGame) return Optional<Uri>.None;
        return ThunderstoreUrls.GetCommunityUri(thunderstoreGame.ThunderstoreCommunitySlug);
    }
}
