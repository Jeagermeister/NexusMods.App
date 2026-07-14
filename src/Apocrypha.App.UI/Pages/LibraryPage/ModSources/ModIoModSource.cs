using DynamicData.Kernel;
using JetBrains.Annotations;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModSources;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.App.UI.Pages.LibraryPage.ModSources;

/// <summary>
/// <see cref="IModSource"/> adapter for mod.io: a game supports it when it declares an
/// <see cref="IModIoGame"/> hub, and browsing goes to that hub's mod.io page. Gated behind the
/// experimental <see cref="ModIoSettings.EnableModIo"/> flag — the paste-a-link entry is mod.io's
/// only in-app surface, so hiding it is what "disabled" means.
/// </summary>
[UsedImplicitly]
internal sealed class ModIoModSource : IModSource
{
    private readonly ISettingsManager _settingsManager;

    public ModIoModSource(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public ModSourceId Id => ModSourceId.ModIo;
    public string DisplayName => "mod.io";
    public bool IsEnabled => _settingsManager.Get<ModIoSettings>().EnableModIo;

    public bool SupportsGame(IGameData game) => game is IModIoGame;

    public Optional<Uri> GetBrowseUri(IGameData game)
    {
        if (game is not IModIoGame modIoGame) return Optional<Uri>.None;
        return ModIoUrls.GetGamePageUri(modIoGame.ModIoGameNameId);
    }
}
