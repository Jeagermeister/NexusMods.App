using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.CLI.OptionParsers;
using Apocrypha.CLI.Types;
using Apocrypha.CLI.Types.IpcHandlers;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.ProxyConsole;

namespace Apocrypha.CLI;

/// <summary>
/// Extension class for <see cref="IServiceCollection"/>
/// </summary>
public static class Services
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Adds the CLI services to the <see cref="IServiceCollection"/>
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddCLI(this IServiceCollection services)
    {
        services.AddOptionParser<AbsolutePath, AbsolutePathParser>()
                .AddOptionParser<IGame, GameParser>()
                .AddOptionParser<Loadout.ReadOnly, LoadoutParser>()
                .AddOptionParser<Uri>(u => (new Uri(u), null))
                .AddOptionParser<Version>(v => (Version.Parse(v), null))
                .AddOptionParser<string>(s => (s, null))
                .AddOptionParser<long>(l => (long.Parse(l), null))
                .AddOptionParser<Matcher, MatcherParser>()
                .AddOptionParser<ITool, ToolParser>();

        // Protocol Handlers
        services.AddSingleton<IIpcProtocolHandler, NxmIpcProtocolHandler>();
        services.AddSingleton<IIpcProtocolHandler, Ror2mmIpcProtocolHandler>();

        // Registers an OS-level URI scheme handler for every enabled IIpcProtocolHandler above.
        services.AddHostedService<UriSchemeRegistration>();

        services.AddProtocolVerbs();
        return services;
    }

}
