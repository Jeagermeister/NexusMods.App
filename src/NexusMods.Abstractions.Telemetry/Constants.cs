using JetBrains.Annotations;
using NexusMods.Sdk;

namespace NexusMods.Abstractions.Telemetry;

[PublicAPI]
internal static class Constants
{
    // Upstream pinned this for Mixpanel event continuity; the phone-home is gone (rebrand
    // R2), so the value only names the in-process OTel meter/service now.
    public const string ApplicationName = "Apocrypha";

    public static string ServiceName => ApplicationName.ToLowerInvariant();
    public static string ServiceVersion => ApplicationConstants.Version.ToSafeString(maxFieldCount: 3);
    public static string MeterName => ServiceName;
}
