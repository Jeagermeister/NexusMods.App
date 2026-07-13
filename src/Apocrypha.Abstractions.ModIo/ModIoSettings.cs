using JetBrains.Annotations;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// Settings for mod.io support. Experimental while the integration matures: enabled by
/// default in debug builds, off in release (same posture as Thunderstore).
/// </summary>
public record ModIoSettings : ISettings
{
    /// <summary>
    /// Enables mod.io as a mod source (paste-a-link installs and related features).
    /// </summary>
    public bool EnableModIo { get; [UsedImplicitly] set; } = ApplicationConstants.IsDebug;

    /// <summary>
    /// The user's mod.io API key (free, from mod.io/me/access). Read-only access: browsing
    /// and downloading only. Not surfaced on the settings page (no free-text container
    /// exists yet, DESIGN-modio.md decision 1) — set via the first-use dialog or the
    /// <c>modio set-api-key</c> CLI verb.
    /// </summary>
    public string ApiKey { get; [UsedImplicitly] set; } = string.Empty;

    /// <inheritdoc/>
    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder
            .ConfigureBackend(StorageBackendOptions.Use(StorageBackends.Json))
            .ConfigureProperty(
                x => x.EnableModIo,
                new PropertyOptions<ModIoSettings, bool>
                {
                    Section = Sections.Experimental,
                    DisplayName = "Enable mod.io support",
                    DescriptionFactory = _ => "Allows downloading mods from mod.io by pasting mod links. Requires a free API key from mod.io/me/access.",
                    RequiresRestart = true,
                },
                new BooleanContainerOptions()
            );
    }
}
