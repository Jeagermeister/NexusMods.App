using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NexusMods.Sdk;

namespace NexusMods.Backend.OS;

internal partial class WindowsInterop
{
    [SupportedOSPlatformGuard("windows")]
    private bool IsWindows() => _os.IsWindows;

    public ValueTask RegisterUriSchemeHandler(string scheme, bool setAsDefaultHandler = true, CancellationToken cancellationToken = default)
    {
        if (!IsWindows()) return ValueTask.CompletedTask;

        // Same guard as LinuxInterop: under a framework-dependent launch (test hosts,
        // `dotnet <dll>`) the process path is the bare dotnet host — registering it would
        // overwrite a working ProgID command with one that swallows every link.
        var runningExecutable = GetRunningExecutablePath(out _);
        if (runningExecutable.FileName is "dotnet" or "dotnet.exe")
        {
            _logger.LogWarning("Skipping URI scheme registration for `{Scheme}`: no apphost binary to point the handler at", scheme);
            return ValueTask.CompletedTask;
        }

        // NOTE(erri120): See this comment for an in-depth guide on protocol handlers:
        // https://github.com/Nexus-Mods/NexusMods.App/pull/1691#issuecomment-2194418849
        // We've decided use the same method that Vortex and MO2 use, which is using a
        // generic ProgID. This means we're always overwriting the existing values.

        try
        {
            RemoveLegacyRegistration(scheme);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Exception while removing pre-rebrand registry entries — continuing, the new registration supersedes them");
        }

        try
        {
            SetAsDefaultHandler(scheme);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception while updating registry to register protocol handler for `{Scheme}`", scheme);
        }

        return ValueTask.CompletedTask;
    }

    private static string CreateProgId(string uriScheme) => $"Apocrypha.{uriScheme}";

    [SupportedOSPlatform("windows")]
    private void RegisterApplication(string uriScheme)
    {
        // https://learn.microsoft.com/en-us/windows/win32/shell/default-programs

        const string capabilitiesPath = @"SOFTWARE\Apocrypha\Capabilities";

        using var key = Registry.CurrentUser.CreateSubKey(capabilitiesPath);
        key.SetValue("ApplicationName", "Apocrypha");
        key.SetValue("ApplicationDescription", "Mod Manager for your games");

        using var urlAssociationsKey = key.CreateSubKey("UrlAssociations");
        urlAssociationsKey.SetValue(uriScheme, CreateProgId(uriScheme));

        using var registeredApplicationsKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\RegisteredApplications");
        registeredApplicationsKey.SetValue("Apocrypha", capabilitiesPath);

        CreateProgIdClass(CreateProgId(uriScheme), $"Apocrypha {uriScheme.ToUpperInvariant()} Handler", isProtocolHandler: false);
    }

    /// <summary>
    /// Best-effort removal of the pre-rebrand HKCU entries (ProgID classes
    /// <c>NexusMods.App.{scheme}</c>, the <c>SOFTWARE\Nexus Mods</c> capabilities tree, and
    /// the old RegisteredApplications value). Untested on a real Windows box — flagged for
    /// the R4 Windows QA pass.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void RemoveLegacyRegistration(string uriScheme)
    {
        Registry.CurrentUser.DeleteSubKeyTree(@$"SOFTWARE\Classes\{ApplicationIdentity.LegacyDataDirectoryName}.{uriScheme}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\Nexus Mods", throwOnMissingSubKey: false);

        using var registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\RegisteredApplications", writable: true);
        registeredApplicationsKey?.DeleteValue(ApplicationIdentity.LegacyDataDirectoryName, throwOnMissingValue: false);
    }

    [SupportedOSPlatform("windows")]
    private void CreateProgIdClass(string progId, string name, bool isProtocolHandler)
    {
        // https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa767914(v=vs.85)

        using var key = Registry.CurrentUser.CreateSubKey(@$"SOFTWARE\Classes\{progId}");
        key.SetValue("", name);
        if (isProtocolHandler) key.SetValue("URL Protocol", "");

        using var commandKey = key.CreateSubKey(@"shell\open\command");

        var executable = GetRunningExecutablePath(out _);
        commandKey.SetValue("", $"\"{executable.ToNativeSeparators(_fileSystem.OS)}\" \"%1\"");

        // NOTE(erri120): can't set the working directory for generic protocol handlers
        // due to possible issues with Vortex/MO2.
        if (!isProtocolHandler) commandKey.SetValue("WorkingDirectory", $"\"{executable.Parent.ToNativeSeparators(_fileSystem.OS)}\"");
    }

    [SupportedOSPlatform("windows")]
    private void SetAsDefaultHandler(string uriScheme)
    {
        CreateProgIdClass(uriScheme, $"Apocrypha {uriScheme.ToUpperInvariant()} Handler", isProtocolHandler: true);
    }
}
