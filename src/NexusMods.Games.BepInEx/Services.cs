using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.BepInEx.Installers;
using NexusMods.Games.BepInEx.Models;
using NexusMods.Games.BepInEx.Schema;
using NexusMods.Sdk.Games;

namespace NexusMods.Games.BepInEx;

/// <summary>
/// Extension methods.
/// </summary>
public static class Services
{
    /// <summary>
    /// Games with a hand-written module claiming their Steam app id; the family must not
    /// double-register them (the Steam locator throws on duplicate app ids). Empty since
    /// PR G folded RoR2 — the Phase 1 pilot — into the family (DESIGN-bepinex-family.md §9);
    /// kept as the seam for any future hand-written override.
    /// </summary>
    private static readonly IReadOnlySet<string> HandWrittenGames = new HashSet<string>();

    /// <summary>
    /// Adds the whole Thunderstore/BepInEx game family to the DI container: one
    /// <see cref="GenericBepInExGame"/> per BepInEx+Steam client instance of the vendored
    /// ecosystem schema (~200 games), each with its own launch tool.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT use <c>AddGame&lt;T&gt;()</c>: that registers one singleton per
    /// <em>type</em>, and every family game shares one class. Each row gets its own set of
    /// factory registrations resolving to one shared instance (design §2.2 edge 1).
    /// </remarks>
    public static IServiceCollection AddBepInExGames(this IServiceCollection services)
    {
        services
            .AddSingleton<BepInExPackInstaller>()
            .AddSingleton<BepInExPluginInstaller>()
            .AddBepInExLoadoutItemModel()
            .AddBepInExPluginLoadoutItemModel();

        foreach (var data in EcosystemSchemaParser.LoadBundledGames(HandWrittenGames))
        {
            var holder = new GameHolder(data);
            services.AddSingleton<IGame>(provider => holder.Get(provider));
            services.AddSingleton<IGameData>(provider => holder.Get(provider));
            services.AddSingleton<ITool>(provider => new RunBepInExGameTool(provider, holder.Get(provider)));
        }

        return services;
    }

    /// <summary>
    /// Makes the three per-game registrations resolve to one shared instance (the
    /// <c>IGame</c>/<c>IGameData</c> identity contract that <c>AddAllSingleton</c> provides
    /// for class-per-game modules).
    /// </summary>
    private sealed class GameHolder(BepInExGameData data)
    {
        private readonly Lock _lock = new();
        private GenericBepInExGame? _instance;

        public GenericBepInExGame Get(IServiceProvider provider)
        {
            lock (_lock)
            {
                return _instance ??= new GenericBepInExGame(provider, data);
            }
        }
    }
}
