using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Apocrypha.App.UI.Controls.ProgressRing;

public partial class ProgressRing : UserControl
{
    public ProgressRing()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

