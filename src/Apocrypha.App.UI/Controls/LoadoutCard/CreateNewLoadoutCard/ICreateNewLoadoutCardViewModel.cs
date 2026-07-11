using System.Reactive;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.LoadoutCard;

public interface ICreateNewLoadoutCardViewModel : IViewModelInterface
{
    ReactiveCommand<Unit, Unit> AddLoadoutCommand { get; }
}
