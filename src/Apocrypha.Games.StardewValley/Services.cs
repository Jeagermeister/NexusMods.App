using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.StardewValley.Emitters;
using Apocrypha.Games.StardewValley.Installers;
using Apocrypha.Games.StardewValley.Models;
using Apocrypha.Games.StardewValley.RunGameTools;
using Apocrypha.Games.StardewValley.WebAPI;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Games.StardewValley;

public static class Services
{
    public static IServiceCollection AddStardewValley(this IServiceCollection services)
    {
        return services
            .AddGame<StardewValley>()
            .AddSingleton<ITool, SmapiRunGameTool>()

            // Installers
            .AddSingleton<SMAPIInstaller>()
            .AddSingleton<GenericInstaller>()

            // Diagnostics
            .AddSingleton<DependencyDiagnosticEmitter>()
            .AddSingleton<MissingSMAPIEmitter>()
            .AddSingleton<SMAPIModDatabaseCompatibilityDiagnosticEmitter>()
            .AddSingleton<SMAPIGameVersionDiagnosticEmitter>()
            .AddSingleton<VersionDiagnosticEmitter>()
            .AddSingleton<ModOverwritesGameFilesEmitter>()

            // Attributes
            .AddSMAPILoadoutItemModel()
            .AddSMAPIModDatabaseLoadoutFileModel()
            .AddSMAPIModLoadoutItemModel()
            .AddSMAPIManifestLoadoutFileModel()

            // Misc
            .AddSingleton<ISMAPIWebApi, SMAPIWebApi>()
            .AddSettings<StardewValleySettings>()

            // Pipelines
            .AddPipelines();
    }
}
