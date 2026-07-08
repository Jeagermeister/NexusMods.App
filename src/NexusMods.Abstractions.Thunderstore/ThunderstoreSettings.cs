using JetBrains.Annotations;
using NexusMods.Sdk;
using NexusMods.Sdk.Settings;

namespace NexusMods.Abstractions.Thunderstore;

/// <summary>
/// Settings for Thunderstore support. Experimental while the integration matures:
/// enabled by default in debug builds, off in release.
/// </summary>
public record ThunderstoreSettings : ISettings
{
    /// <summary>
    /// Enables Thunderstore as a mod source (ror2mm:// one-click links and related features).
    /// </summary>
    public bool EnableThunderstore { get; [UsedImplicitly] set; } = ApplicationConstants.IsDebug;

    /// <inheritdoc/>
    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder
            .ConfigureBackend(StorageBackendOptions.Use(StorageBackends.Json))
            .ConfigureProperty(
                x => x.EnableThunderstore,
                new PropertyOptions<ThunderstoreSettings, bool>
                {
                    Section = Sections.Experimental,
                    DisplayName = "Enable Thunderstore support",
                    DescriptionFactory = _ => "Allows downloading mods from thunderstore.io, including ror2mm:// one-click install links.",
                    RequiresRestart = true,
                },
                new BooleanContainerOptions()
            );
    }
}
