using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;

namespace Apocrypha.Abstractions.Games;

/// <summary>
///     Adds games related serialization services.
/// </summary>
public static class Services
{
    /// <summary>
    ///     Adds known Game entity related serialization services.
    /// </summary>
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        return services
            // Transient, NOT singleton: each game resolves its own manager (once, cached by the
            // game's Lazy<ISortOrderManager>) and registers ITS varieties into it. A shared
            // singleton was clobbered on every RegisterSortOrderVarieties call — opening a second
            // game's page replaced the first game's varieties (and leaked its change subscription),
            // silently breaking e.g. Cyberpunk's REDmod load order for the rest of the session.
            .AddTransient<SortOrderManager>()
            .AddSortOrderItemModel()
            .AddSortOrderQueriesSql()
            .AddLoadoutItemGroupPriorityModel();
    }
}
