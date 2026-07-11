using JetBrains.Annotations;

namespace Apocrypha.Sdk.Settings;

[PublicAPI]
public record OverrideHack(Type SettingsType, Func<object, object> Method, string? Key);
