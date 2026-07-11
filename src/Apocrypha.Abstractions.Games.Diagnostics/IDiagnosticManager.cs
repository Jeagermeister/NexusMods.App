using JetBrains.Annotations;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Abstractions.Diagnostics;

/// <summary>
/// A diagnostic manager, which keeps track of all current diagnostics and refreshes them if necessary.
/// </summary>
[PublicAPI]
public interface IDiagnosticManager : IDisposable
{
    /// <summary>
    /// Returns an observable stream of all diagnostics for a loadout.
    /// </summary>
    IObservable<Diagnostic[]> GetLoadoutDiagnostics(LoadoutId loadoutId);

    /// <summary>
    /// Returns the counts of the diagnostics per severity level.
    /// </summary>
    IObservable<(int NumSuggestions, int NumWarnings, int NumCritical)> CountDiagnostics(LoadoutId loadoutId);
}
