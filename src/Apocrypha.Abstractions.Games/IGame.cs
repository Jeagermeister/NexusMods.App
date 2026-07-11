using DynamicData.Kernel;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Abstractions.Games;

/// <summary>
/// Interface for a specific game recognized by the app. A single game can have
/// multiple installations.
/// </summary>
public interface IGame : IGameData
{
    /// <summary>
    /// Gets all available installers this game supports.
    /// </summary>
    ILibraryItemInstaller[] LibraryItemInstallers { get; }

    /// <summary>
    /// An array of all instances of <see cref="IDiagnosticEmitter"/> supported
    /// by the game.
    /// </summary>
    IDiagnosticEmitter[] DiagnosticEmitters { get; }

    /// <summary>
    /// The synchronizer for this game.
    /// </summary>
    ILoadoutSynchronizer Synchronizer { get; }
    
    /// <summary>
    /// The sort order manager for this game.
    /// </summary>
    ISortOrderManager SortOrderManager { get; }
}
