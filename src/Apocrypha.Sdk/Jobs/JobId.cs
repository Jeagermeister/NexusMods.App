using JetBrains.Annotations;
using TransparentValueObjects;

namespace Apocrypha.Sdk.Jobs;

/// <summary>
/// Represents the ID of a job.
/// </summary>
[PublicAPI]
[ValueObject<Guid>]
public readonly partial struct JobId : IAugmentWith<DefaultValueAugment, JsonAugment>
{
    /// <inheritdoc/>
    public static JobId DefaultValue { get; } = From(Guid.Empty);
}
