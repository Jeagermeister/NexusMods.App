using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.App.UI.Controls.Settings.Section;

public interface ISettingSectionViewModel : IViewModelInterface
{
    SectionDescriptor Descriptor { get; }
}
