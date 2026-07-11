using JetBrains.Annotations;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.UI.Sdk.Settings;

[PublicAPI]
public class BooleanContainer : APropertyValueContainer<bool, BooleanContainerOptions>
{
    public BooleanContainer(
        bool value,
        bool defaultValue,
        PropertyConfig config) : base(value, defaultValue, config) { }
}
