namespace Apocrypha.CLI.Types;

/// <summary>
/// Defines a protocol handler for protocols which need passing messages to the main application.
/// </summary>
public interface IIpcProtocolHandler
{
    /// <summary>
    /// The protocol to handle, e.g. 'nxm'
    /// </summary>
    public string Protocol { get; }

    /// <summary>
    /// Whether this handler is currently enabled. Disabled handlers are skipped by OS scheme
    /// registration and should no-op in <see cref="Handle"/>. Defaults to true; settings-gated
    /// handlers (e.g. experimental mod sources) override this.
    /// </summary>
    public bool IsEnabled => true;

    /// <summary>
    /// Handles the given URL.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="token">Allows to cancel the operation.</param>
    public Task Handle(string url, CancellationToken token);
}
