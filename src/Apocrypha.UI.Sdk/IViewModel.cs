using ReactiveUI;

namespace Apocrypha.UI.Sdk;

public interface IViewModel : IActivatableViewModel
{
    public Type ViewModelInterface { get; }
}
