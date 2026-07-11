using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Loadouts.Sorting;

[PublicAPI]
public record Last<TType, TId> : ISortRule<TType, TId>;
