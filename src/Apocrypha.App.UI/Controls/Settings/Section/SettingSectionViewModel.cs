using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.App.UI.Controls.Settings.Section;

public class SettingSectionViewModel : AViewModel<ISettingSectionViewModel>, ISettingSectionViewModel
{
    public SectionDescriptor Descriptor { get; }

    public SettingSectionViewModel(SectionDescriptor descriptor)
    {
        Descriptor = descriptor;
    }
}
