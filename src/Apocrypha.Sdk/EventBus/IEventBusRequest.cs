using JetBrains.Annotations;

namespace Apocrypha.Sdk.EventBus;

/// <summary>
/// Represents a request.
/// </summary>
[PublicAPI]
public interface IEventBusRequest<TResult> : IEventBusMessage
    where TResult : notnull;
