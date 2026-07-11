using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Abstractions.Games;

/// <summary>
/// Several extensions for game related classes
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Returns the game of a game installation, casted into an IGame for convenience.
    /// </summary>
    /// <param name="gameInstallation"></param>
    /// <returns></returns>
    public static IGame GetGame(this GameInstallation gameInstallation) => (IGame)gameInstallation.Game;

    /// <summary>
    /// Adds the given game to the DI system
    /// </summary>
    /// <param name="collection"></param>
    /// <typeparam name="TGame"></typeparam>
    /// <returns></returns>
    public static IServiceCollection AddGame<TGame>(this IServiceCollection collection) where TGame : class, IGame
    {
        collection.AddAllSingleton<IGame, TGame>();
        collection.AddSingleton<IGameData, TGame>(s => s.GetRequiredService<TGame>());
        return collection;
    }
}
