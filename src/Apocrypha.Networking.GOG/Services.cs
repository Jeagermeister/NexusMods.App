using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.GOG;
using Apocrypha.Abstractions.GOG.Values;
using Apocrypha.Networking.GOG.CLI;
using Apocrypha.Networking.GOG.Models;
using Apocrypha.Sdk.ProxyConsole;

namespace Apocrypha.Networking.GOG;

public static class Services
{
    public static IServiceCollection AddGOG(this IServiceCollection services)
    {
        services.AddGOGVerbs();
        services.AddSingleton<IClient, Client>();
        services.AddAuthInfoModel();
        services.AddOptionParser(s => ProductId.From(ulong.Parse(s)));
        return services;
    }
}
