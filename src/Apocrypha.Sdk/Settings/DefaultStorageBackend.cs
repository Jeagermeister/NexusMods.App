using JetBrains.Annotations;

namespace Apocrypha.Sdk.Settings;

[PublicAPI]
public record DefaultStorageBackend(IBaseStorageBackend Backend);
