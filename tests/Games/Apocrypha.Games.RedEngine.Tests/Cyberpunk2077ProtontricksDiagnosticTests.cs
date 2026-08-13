using CliWrap;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Games.RedEngine.Cyberpunk2077.Emitters;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Xunit;
using Xunit.Abstractions;
using static Apocrypha.Games.RedEngine.Constants;

namespace Apocrypha.Games.RedEngine.Tests;

/// <summary>
/// Covers <see cref="MissingProtontricksForRedModEmitter"/>, which had no test coverage.
///
/// Protontricks is only needed to launch <c>redMod.exe</c> inside the Proton prefix, so the
/// diagnostic must fire when the REDmod tool is installed and Protontricks is not — and must stay
/// quiet when there is no REDmod tool to run. Both tests fail against the pre-fix emitter, which
/// tested <c>redModPath.FileExists</c> instead of <c>!redModPath.FileExists</c>.
///
/// Fully offline: Protontricks is made "not installed" by substituting <see cref="IProcessRunner"/>,
/// so this needs no network, no Nexus API key and no protocol-handler registration.
/// </summary>
public class Cyberpunk2077ProtontricksDiagnosticTests(ITestOutputHelper outputHelper)
    : ACyberpunkIsolatedGameTest<Cyberpunk2077ProtontricksDiagnosticTests>(outputHelper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        // Registered last so it wins over the real ProcessRunner from AddBackend.
        return base.AddServices(services)
            .AddSingleton<IProcessRunner, ProtontricksNotInstalledProcessRunner>();
    }

    private MissingProtontricksForRedModEmitter Emitter
        => Game.DiagnosticEmitters.OfType<MissingProtontricksForRedModEmitter>().Single();

    [Fact]
    public async Task EmitsWhenRedModToolIsPresentAndProtontricksIsMissing()
    {
        // The emitter no-ops off Linux, where Protontricks is not a concept.
        if (!OperatingSystem.IsLinux()) return;

        var loadout = await CreateLoadout();
        await CreateRedModToolAsync();

        var diagnostics = await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().ContainSingle(
            "redMod.exe is present and Protontricks is what runs it inside the prefix, so the user must be told it is missing"
        );
    }

    [Fact]
    public async Task DoesNotEmitWhenRedModToolIsAbsent()
    {
        if (!OperatingSystem.IsLinux()) return;

        var loadout = await CreateLoadout();
        // Deliberately no redMod.exe on disk.

        var diagnostics = await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().BeEmpty(
            "with no REDmod tool installed there is nothing for Protontricks to launch, so demanding it is noise"
        );
    }

    private async Task CreateRedModToolAsync()
    {
        var redModPath = GameInstallation.Locations.ToAbsolutePath(RedModPath);
        redModPath.Parent.CreateDirectory();
        await redModPath.WriteAllTextAsync("stub redMod.exe");
    }

    /// <summary>
    /// Stands in for a machine without Protontricks. Throwing is enough: the query runs inside
    /// <c>AggregateExecutableRuntimeDependency.GetActiveDependencyAsync</c>, which swallows the
    /// exception and reports the dependency as unavailable — the same result as a non-zero exit.
    /// </summary>
    private sealed class ProtontricksNotInstalledProcessRunner : IProcessRunner
    {
        public void Run(Command command, bool logOutput = false)
            => throw new NotSupportedException("no processes should be started by this test");

        public Task<CommandResult> RunAsync(Command command, bool logOutput = false, CancellationToken cancellationToken = default)
            => throw new FileNotFoundException($"`{command.TargetFilePath}` is not installed");

        public Task RunAsync(System.Diagnostics.Process process, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("no processes should be started by this test");

        public int CleanupOldLogs() => 0;
    }
}
