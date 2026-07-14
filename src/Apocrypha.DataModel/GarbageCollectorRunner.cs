using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.GC;
using Apocrypha.Sdk.Settings;
using Apocrypha.App.GarbageCollection.DataModel;
using Apocrypha.CrossPlatform;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk;

namespace Apocrypha.DataModel;

/// <inheritdoc />
public class GarbageCollectorRunner(ISettingsManager settings, NxFileStore store, IConnection connection, ILogger<GarbageCollectorRunner> logger) : IGarbageCollectorRunner
{
    private readonly DataModelSettings _settings = settings.Get<DataModelSettings>();
    private readonly NxFileStore _store = store;
    private readonly IConnection _connection = connection;
    private readonly ILogger<GarbageCollectorRunner> _logger = logger;

    private readonly object _coalesceLock = new();
    private DateTime _lastRunUtc = DateTime.MinValue;

    /// <inheritdoc />
    public void Run()
    {
        RunGarbageCollector.Do(_logger, _settings.ArchiveLocations, _store, _connection);
    }
    
    /// <inheritdoc />
    public Task RunAsync()
    {
        return Task.Run(Run);
    }

    /// <inheritdoc />
    public Task RunCoalescedAsync(TimeSpan minInterval)
    {
        lock (_coalesceLock)
        {
            if (DateTime.UtcNow - _lastRunUtc < minInterval)
                return Task.CompletedTask;

            // Claim the slot before running so concurrent applies (for other games) skip rather
            // than kick off a second archive repack over the same store at the same time.
            _lastRunUtc = DateTime.UtcNow;
        }

        return RunAsync();
    }
    
    /// <inheritdoc />
    public async Task RunWithMode(GarbageCollectorRunMode gcRunMode)
    {
        switch (gcRunMode)
        {
            case GarbageCollectorRunMode.RunSynchronously:
                Run();
                break;
            case GarbageCollectorRunMode.RunAsyncInBackground:
                RunAsync().FireAndForget(_logger);
                break;
            case GarbageCollectorRunMode.RunAsynchronously:
                await RunAsync();
                break;
            case GarbageCollectorRunMode.DoNotRun:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gcRunMode), gcRunMode, null);
        }
    }
}
