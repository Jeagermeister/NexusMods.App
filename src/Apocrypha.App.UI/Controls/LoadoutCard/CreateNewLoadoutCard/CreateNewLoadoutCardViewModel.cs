using System.Reactive;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.LoadoutCard;

public class CreateNewLoadoutCardViewModel : AViewModel<ICreateNewLoadoutCardViewModel>, ICreateNewLoadoutCardViewModel
{
    public required ReactiveCommand<Unit, Unit> AddLoadoutCommand { get; init; } 
}
