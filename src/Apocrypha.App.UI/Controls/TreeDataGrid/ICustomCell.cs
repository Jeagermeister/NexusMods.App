using Avalonia.Controls.Models.TreeDataGrid;

namespace Apocrypha.App.UI.Controls;

public interface ICustomCell : ICell
{
    public string Id { get; }
    public bool IsRoot { get; }
}
