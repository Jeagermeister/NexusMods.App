using R3;

namespace Apocrypha.App.UI.Controls;

public interface ITreeDataGirdMessageAdapter<TMessage>
    where TMessage : notnull
{
    Subject<TMessage> MessageSubject { get; }
}
