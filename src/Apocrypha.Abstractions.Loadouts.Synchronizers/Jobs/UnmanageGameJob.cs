using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// The specified game installation is being unmanaged and the files are being reset to their original state
/// </summary>
public record UnmanageGameJob(GameInstallation Installation) : IJobDefinition<GameInstallation>;
