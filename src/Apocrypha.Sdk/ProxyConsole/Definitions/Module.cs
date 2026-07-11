using JetBrains.Annotations;

namespace Apocrypha.Sdk.ProxyConsole;

/// <summary>
/// Documentation for a collection of verbs
/// </summary>
[PublicAPI]
public record ModuleDefinition(string Name, string Description);
