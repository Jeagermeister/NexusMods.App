using TransparentValueObjects;

namespace Apocrypha.Abstractions.Steam.Values;

/// <summary>
/// A globally unique identifier for an application on Steam.
/// </summary>
[ValueObject<uint>]
public readonly partial struct AppId : IAugmentWith<JsonAugment>
{
    
}
