using JetBrains.Annotations;
using Apocrypha.Sdk.Settings;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.UI.Sdk.Settings;

[PublicAPI]
public record SectionDescriptor(
    SectionId Id,
    string Name,
    Func<IconValue> IconFunc,
    uint Priority = 0,
    bool Hidden = false
);
