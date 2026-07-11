using Apocrypha.Sdk;
using R3;

namespace Apocrypha.App.UI.Overlays.Updater;

public interface IUpdaterViewModel : IOverlayViewModel
{
    ReactiveCommand CommandClose { get; }
    ReactiveCommand CommandOpenReleaseInBrowser { get; }
    ReactiveCommand CommandDownloadReleaseAssetInBrowser { get; }
    bool HasAsset { get; }

    Version CurrentVersion { get; }
    Version LatestVersion { get; }
    InstallationMethod InstallationMethod { get; }
}
