using TransparentValueObjects;
namespace Apocrypha.Abstractions.GOG.Values;

[ValueObject<ulong>]
public  readonly partial struct ProductId : IAugmentWith<JsonAugment>
{
    
}
