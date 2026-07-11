using Apocrypha.Sdk;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.UI.Sdk.Settings;

public class ConfigurablePathsContainer : APropertyValueContainer<ConfigurablePath[], ConfigurablePathsContainerOption>
{
    public ConfigurablePathsContainer(
        ConfigurablePath[] value,
        ConfigurablePath[] defaultValue,
        PropertyConfig config) : base(value, defaultValue, config)
    {
    }
}
