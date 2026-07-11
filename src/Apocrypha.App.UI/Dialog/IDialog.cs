using Avalonia.Controls;
using Apocrypha.App.UI.Dialog.Enums;

namespace Apocrypha.App.UI.Dialog;

public interface IDialog
{
    public Task<StandardDialogResult> Show(Window owner, bool isModal = true);
}
