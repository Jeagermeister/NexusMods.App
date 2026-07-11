using Apocrypha.Abstractions.Serialization.Attributes;

namespace Apocrypha.Abstractions.Loadouts.Sorting;

/// <summary />
[JsonName("NexusMods.Abstractions.DataModel.Entities.Sorting.Before")]
public record Before<TType, TId>: ISortRule<TType, TId>
{
    /// <summary>
    ///     ID of the other mod this mod should be placed before.
    /// </summary>
    public required TId Other { get; init; }
}
