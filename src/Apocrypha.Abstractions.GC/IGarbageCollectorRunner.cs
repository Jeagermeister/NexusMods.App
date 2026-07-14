namespace Apocrypha.Abstractions.GC;

/// <summary>
/// Utility for running the garbage collection process.
/// </summary>
public interface IGarbageCollectorRunner
{
    /// <summary>
    /// Starts the garbage collector.
    /// </summary>
    void Run();
    
    /// <summary>
    /// Runs the Garbage Collector asynchronously.
    /// </summary>
    Task RunAsync();

    /// <summary>
    /// Runs the Garbage Collector, but only if a previous run has not completed within
    /// <paramref name="minInterval"/>; otherwise returns without running. Intended for the apply
    /// hot path, which would otherwise run a full archive scan+repack after every single sync.
    /// Runs inline (never concurrently with the caller), so it only ever *skips* the same safe
    /// operation — unreferenced data skipped now is reclaimed by the next run.
    /// </summary>
    Task RunCoalescedAsync(TimeSpan minInterval);

    /// <summary>
    /// Runs the Garbage Collector in the specified mode.
    /// </summary>
    /// <param name="gcRunMode">The mode to run the GC in.</param>
    // ReSharper disable once InconsistentNaming
    Task RunWithMode(GarbageCollectorRunMode gcRunMode);
}
