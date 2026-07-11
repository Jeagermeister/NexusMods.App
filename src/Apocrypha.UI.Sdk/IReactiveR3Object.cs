using JetBrains.Annotations;
using R3;

namespace Apocrypha.UI.Sdk;

[PublicAPI]
public interface IReactiveR3Object : ReactiveUI.IReactiveObject, IDisposable
{
    Observable<bool> Activation { get; }
    bool IsActivated { get; }
    IDisposable Activate();
    void Deactivate();
    bool IsDisposed { get; }
}
