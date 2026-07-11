using JetBrains.Annotations;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.UI.Sdk.Settings;

[PublicAPI]
public interface IInteractionControl : IViewModelInterface
{
    IPropertyValueContainer ValueContainer { get; }
}

[PublicAPI]
public interface IInteractionControlFactory<in TContainerOptions>
    where TContainerOptions : IContainerOptions
{
    IInteractionControl Create(IServiceProvider serviceProvider, ISettingsManager settingsManager, TContainerOptions containerOptions, PropertyConfig propertyConfig);
}
