using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Settings;
using Apocrypha.Games.Larian.BaldursGate3.Emitters;
using Apocrypha.Games.Larian.BaldursGate3.RunGameTools;

namespace Apocrypha.Games.Larian.BaldursGate3;

public static class Services
{
    public static IServiceCollection AddBaldursGate3(this IServiceCollection services)
    {
        services
            .AddGame<BaldursGate3>()
            .AddSingleton<ITool, BG3RunGameTool>()
            .AddSettings<BaldursGate3Settings>()
            .AddPipelines()
            // diagnostics
            .AddSingleton<DependencyDiagnosticEmitter>();

        return services;
    }
}
