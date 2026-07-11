using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// A job to create a loadout
/// </summary>
public record CreateLoadoutJob(GameInstallation Installation) : IJobDefinition<Loadout.ReadOnly>;
