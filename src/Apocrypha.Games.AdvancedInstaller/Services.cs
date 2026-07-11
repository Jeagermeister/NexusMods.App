using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Library.Installers;

namespace Apocrypha.Games.AdvancedInstaller;

public static class Services
{
    public static IServiceCollection AddAdvancedInstaller(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddTransient<ILibraryArchiveInstaller, AdvancedManualInstaller>()
            .AddKeyedTransient<ILibraryItemInstaller, AdvancedManualInstaller>(serviceKey: nameof(AdvancedManualInstaller), (serviceProvider, _) => new AdvancedManualInstaller(serviceProvider, isDirect: false))
            .AddKeyedTransient<ILibraryItemInstaller, AdvancedManualInstaller>(serviceKey: $"{nameof(AdvancedManualInstaller)}_Direct", (serviceProvider, _) => new AdvancedManualInstaller(serviceProvider, isDirect: true));
    }
}
