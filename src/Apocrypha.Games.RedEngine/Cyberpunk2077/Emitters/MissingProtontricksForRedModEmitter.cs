using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Diagnostics.Values;
using Apocrypha.Games.Generic.Dependencies;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using static Apocrypha.Games.RedEngine.Constants;
using System.Runtime.CompilerServices;
namespace Apocrypha.Games.RedEngine.Cyberpunk2077.Emitters;

public partial class MissingProtontricksForRedModEmitter : ILoadoutDiagnosticEmitter
{
    public static readonly NamedLink ProtontricksLink = new("Protontricks Installation Guide", new Uri("https://github.com/Matoking/protontricks?tab=readme-ov-file#installation"));
    
    /// <summary>
    /// This will be null on non-Linux OSes.
    /// </summary>
    private AggregateProtontricksDependency? _protontricksDependency;
    
    /// <summary/>
    public MissingProtontricksForRedModEmitter(IServiceProvider serviceProvider) => _protontricksDependency = serviceProvider.GetService<AggregateProtontricksDependency>();

    public async IAsyncEnumerable<Diagnostic> Diagnose(
        Loadout.ReadOnly loadout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var install = loadout.InstallationInstance;
        var locations = install.Locations;
        var redModPath = locations.ToAbsolutePath(RedModPath);

        if (!FileSystem.Shared.OS.IsLinux || _protontricksDependency == null)
            yield break;

        // If there is no REDmod EXE, we don't need Protontricks.
        if (redModPath.FileExists)
            yield break;

        var installInfo = await _protontricksDependency.QueryInstallationInformation(cancellationToken);
        if (!installInfo.HasValue)
            yield return Diagnostics.CreateMissingProtontricksForRedMod(ProtontricksLink);
    }
}
