using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Sdk.Settings;

namespace NexusMods.Sdk.Tracking;

public record TrackingSettings : ISettings
{
    /// <remarks>
    /// Apocrypha: telemetry was removed (KIRO-HANDOFF §23.4) — nothing observes this flag
    /// anymore and the settings UI no longer exposes it. The property survives only so
    /// existing settings JSON keeps deserializing.
    /// </remarks>
    public bool EnableTracking { get; [UsedImplicitly] set; }

    public Guid DeviceId { get; set; }

    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder
            .ConfigureBackend(StorageBackendOptions.Use(StorageBackends.Json))
            .ConfigureDefault(CreateDefault);
    }

    private static TrackingSettings CreateDefault(IServiceProvider serviceProvider)
    {
        var timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        return new TrackingSettings
        {
            EnableTracking = false,
            DeviceId = Guid.CreateVersion7(timeProvider.GetUtcNow()),
        };
    }
}
