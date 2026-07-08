using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Job wrapping local game version recognition (see <see cref="ILocalGameVersionRecognizer"/>).
/// Running recognition as a job lets it survive UI navigation, exposes progress, and lets views
/// detect an in-flight run for a given installation instead of starting a duplicate one.
/// </summary>
/// <remarks>Linux fork: part of the login-free local recognition pipeline.</remarks>
public record RecognizeGameVersionJob(GameInstallation Installation) : IJobDefinition<LocalRecognitionResult>;
