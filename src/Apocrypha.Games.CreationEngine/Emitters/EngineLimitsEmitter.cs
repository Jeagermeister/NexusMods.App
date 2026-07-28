using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.CreationEngine.Emitters;

/// <summary>
/// Warns when the enabled plugins load more general archives than the engine can handle.
/// </summary>
/// <remarks>
/// <para>
/// The overrun does not stop the game from launching. Archive and file-handle lookups start
/// failing at runtime instead, which presents as garbage textures, missing assets, or an access
/// violation at the main menu — symptoms that point everywhere except the real cause. On a real
/// 908-mod collection this cost a full evening of bisecting innocent mods before the archive
/// count was identified; this emitter is that lesson as a one-line warning.
/// </para>
/// <para>
/// The warning suppresses itself when Buffout 4 is deployed with both mitigations configured
/// (<c>ArchiveLimit = true</c> and a raised <c>MaxStdIO</c>), so users who have already fixed
/// the problem are not nagged about it.
/// </para>
/// </remarks>
public class EngineLimitsEmitter : ILoadoutDiagnosticEmitter
{
    /// <summary>
    /// The stdio cap that actually mitigates the handle exhaustion. Also the maximum msvcr110
    /// accepts — larger values make the CRT fail-fast on startup, so this is both floor and target.
    /// </summary>
    public const int RequiredMaxStdIO = 2048;

    private static readonly GamePath BuffoutConfigPath = new(LocationId.Game, "Data/F4SE/Plugins/Buffout4/config.toml");

    private readonly ICreationEngineGame _game;
    private readonly IStreamSourceDispatcher _streamSource;

    public EngineLimitsEmitter(ICreationEngineGame game, IStreamSourceDispatcher streamSource)
    {
        _game = game;
        _streamSource = streamSource;
    }

    public IAsyncEnumerable<Diagnostic> Diagnose(Loadout.ReadOnly loadout, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<Diagnostic> Diagnose(Loadout.ReadOnly loadout, FrozenDictionary<GamePath, SyncNode> syncTree, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_game.MaxGeneralArchives is not { } limit)
            yield break;

        // Plugin stems and archive names from the winning files — the same view of the world
        // PluginsFile.Write deploys from, so the count matches what the engine will actually load.
        List<string> pluginStems = [];
        List<string> archiveNames = [];

        foreach (var (path, node) in syncTree)
        {
            if (!node.HaveLoadout || path.Parent != KnownPaths.Data)
                continue;

            if (KnownCEExtensions.PluginFiles.Contains(path.Extension))
                pluginStems.Add(path.Path.FileName.ToString()[..^path.Extension.ToString().Length]);
            else if (path.Extension == KnownCEExtensions.BA2 || path.Extension == KnownCEExtensions.BSA)
                archiveNames.Add(path.Path.FileName);
        }

        var archiveCount = CountArchivesOwnedByPlugins(archiveNames, pluginStems);
        if (archiveCount <= limit)
            yield break;

        var mitigation = await ReadBuffoutMitigation(syncTree, cancellationToken);
        if (mitigation.IsFullyMitigated)
            yield break;

        yield return Diagnostics.CreateTooManyArchives(
            archiveCount,
            limit,
            mitigation.Describe()
        );
    }

    /// <summary>
    /// Counts the archives the engine will load: those whose name prefix (the part before
    /// <c>" - "</c>, e.g. <c>SomeMod - Textures.ba2</c>) matches an enabled plugin's stem.
    /// </summary>
    public static int CountArchivesOwnedByPlugins(IReadOnlyList<string> archiveFileNames, IReadOnlyList<string> pluginStems)
    {
        var stems = new HashSet<string>(pluginStems, StringComparer.OrdinalIgnoreCase);

        var count = 0;
        foreach (var archive in archiveFileNames)
        {
            var separator = archive.IndexOf(" - ", StringComparison.Ordinal);
            var baseName = separator > 0
                ? archive[..separator]
                : Path.GetFileNameWithoutExtension(archive);

            if (stems.Contains(baseName))
                count++;
        }

        return count;
    }

    public readonly record struct BuffoutMitigation(bool ConfigFound, bool ArchiveLimitEnabled, int MaxStdIO)
    {
        public bool IsFullyMitigated => ConfigFound && ArchiveLimitEnabled && MaxStdIO >= RequiredMaxStdIO;

        public string Describe()
        {
            if (!ConfigFound)
                return "Buffout 4 is not installed (no `Buffout4/config.toml` deployed).";

            return $"Buffout 4 is installed, but its config has `ArchiveLimit = {(ArchiveLimitEnabled ? "true" : "false")}` " +
                   $"and `MaxStdIO = {MaxStdIO}` — both must be set as below for the limit to be safe.";
        }
    }

    private async ValueTask<BuffoutMitigation> ReadBuffoutMitigation(FrozenDictionary<GamePath, SyncNode> syncTree, CancellationToken cancellationToken)
    {
        // GamePath comparison folds case, so this lookup also finds configs deployed with
        // different directory casing.
        if (!syncTree.TryGetValue(BuffoutConfigPath, out var node) || !node.HaveLoadout)
            return new BuffoutMitigation(ConfigFound: false, ArchiveLimitEnabled: false, MaxStdIO: 0);

        try
        {
            await using var stream = await _streamSource.OpenAsync(node.Loadout.Hash);
            if (stream is null)
                return new BuffoutMitigation(ConfigFound: false, ArchiveLimitEnabled: false, MaxStdIO: 0);

            using var reader = new StreamReader(stream);
            return ParseBuffoutConfig(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (Exception)
        {
            // An unreadable config is reported the same as an unconfigured one — the diagnostic
            // must never be the thing that breaks Apply.
            return new BuffoutMitigation(ConfigFound: false, ArchiveLimitEnabled: false, MaxStdIO: 0);
        }
    }

    /// <summary>
    /// Minimal line-based read of the two Buffout keys this diagnostic cares about. Buffout's
    /// config is flat <c>key = value</c> TOML with <c>#</c> comments; a full TOML parser would be
    /// more dependency than the two keys warrant.
    /// </summary>
    public static BuffoutMitigation ParseBuffoutConfig(string content)
    {
        var archiveLimit = false;
        var maxStdIo = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;
            var comment = line.IndexOf('#');
            if (comment >= 0)
                line = line[..comment];

            var separator = line.IndexOf('=');
            if (separator < 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Equals("ArchiveLimit", StringComparison.OrdinalIgnoreCase))
                archiveLimit = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("MaxStdIO", StringComparison.OrdinalIgnoreCase))
                _ = int.TryParse(value, out maxStdIo);
        }

        return new BuffoutMitigation(ConfigFound: true, ArchiveLimitEnabled: archiveLimit, MaxStdIO: maxStdIo);
    }
}
