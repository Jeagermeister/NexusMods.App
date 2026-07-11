using JetBrains.Annotations;

namespace Apocrypha.Sdk.Settings;

[PublicAPI]
public record SettingsRegistration(
    Type ObjectType,
    ISettings DefaultValue,
    Func<ISettingsBuilder, ISettingsBuilder> ConfigureLambda
);
