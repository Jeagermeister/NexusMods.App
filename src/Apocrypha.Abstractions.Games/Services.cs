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
            .AddSingleton<SortOrderManager>()
            .AddSortOrderItemModel()
            .AddSortOrderQueriesSql()
            .AddLoadoutItemGroupPriorityModel();
    }
}
