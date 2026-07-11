using System.Collections.ObjectModel;
using Apocrypha.Abstractions.GuidedInstallers;
using Apocrypha.UI.Sdk;

namespace Apocrypha.Games.FOMOD.UI;

public interface IGuidedInstallerGroupViewModel : IViewModelInterface
{
    public IObservable<bool> HasValidSelectionObservable { get; }

    public OptionGroup Group { get; }

    public ReadOnlyObservableCollection<IGuidedInstallerOptionViewModel> Options { get; }

    public IGuidedInstallerOptionViewModel? HighlightedOption { get; set;  }
}
