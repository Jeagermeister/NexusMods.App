using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Settings;
using Apocrypha.Games.MountAndBlade2Bannerlord.Installers;
using Apocrypha.Games.MountAndBlade2Bannerlord.LauncherManager;
using Apocrypha.Games.MountAndBlade2Bannerlord.Models;

namespace Apocrypha.Games.MountAndBlade2Bannerlord;

public static class Services
{
    public static IServiceCollection AddMountAndBlade2Bannerlord(this IServiceCollection services)
    {
        return services
            .AddGame<Bannerlord>()
            .AddSingleton<ITool, BannerlordRunGameTool>()

            // Installers
            .AddSingleton<BLSEInstaller>()
            .AddSingleton<BannerlordModInstaller>()

            // Diagnostics

            // Attributes
            .AddBannerlordModuleLoadoutItemModel()
            .AddModuleInfoFileLoadoutFileModel()
   
            // Misc
            .AddSettings<BannerlordSettings>()
            .AddSingleton<LauncherManagerFactory>()
            .AddSingleton<FileSystemProvider>()
            .AddSingleton<NotificationProvider>()
            .AddSingleton<DialogProvider>()
            
            // Pipelines
            .AddPipelines();
    }
}
