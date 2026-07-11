using NexusMods.Paths;

namespace NexusMods.Sdk.Tests;

/// <summary>
/// Exercises the R3 rebrand migration against a real-filesystem sandbox: an overlay
/// filesystem maps the KnownPath bases into a temp directory, so the atomic
/// Directory.Move and the config rewrite run for real without touching user data.
/// </summary>
public class LegacyDataMigrationTests
{
    // Real persisted-settings shapes from a live pre-rebrand install.
    private const string LegacyDataModelSettingsJson =
        """{"UseInMemoryDataModel":false,"MnemonicDBPath":{"BaseDirectory":"XDG_DATA_HOME","File":"NexusMods.App/DataModel/MnemonicDB.rocksdb"},"ArchiveLocations":[{"BaseDirectory":"XDG_DATA_HOME","File":"NexusMods.App/DataModel/Archives"}]}""";

    private const string LegacyLoggingSettingsJson =
        """{"MainProcessLogFilePath":{"BaseDirectory":"XDG_STATE_HOME","File":"NexusMods.App/Logs/nexusmods.app.main.current.log"},"MaxArchivedFiles":10}""";

    private const string LegacyCliSettingsJson =
        """{"StartCliBackend":true,"SyncFile":"/run/user/1000/NexusMods.App-sync_file.sync"}""";

    private const string HashesSettingsJson =
        """{"HashDatabaseLocation":{"BaseDirectory":"XDG_DATA_HOME","File":"NexusMods.App/FileHashesDatabase"},"GithubManifestUrl":"https://github.com/Nexus-Mods/game-hashes/releases/latest/download/manifest.json"}""";

    [Test]
    public async Task MovesLegacyDirectoryAndRewritesConfigs()
    {
        using var sandbox = Sandbox.Create();
        var oldDirectory = sandbox.LocalAppData.Combine(ApplicationIdentity.LegacyDataDirectoryName);
        WriteFile(oldDirectory.Combine("DataModel/marker.txt"), "loadouts live here");
        WriteFile(oldDirectory.Combine("Configs/NexusMods.DataModel.DataModelSettings.json"), LegacyDataModelSettingsJson);
        WriteFile(oldDirectory.Combine("Configs/NexusMods.Games.FileHashes.FileHashesServiceSettings.json"), HashesSettingsJson);

        var didWork = LegacyDataMigration.Run(sandbox.FileSystem, _ => { });

        await Assert.That(didWork).IsTrue();
        await Assert.That(oldDirectory.DirectoryExists()).IsFalse();

        var newDirectory = sandbox.LocalAppData.Combine(ApplicationIdentity.DataDirectoryName);
        await Assert.That(newDirectory.Combine("DataModel/marker.txt").FileExists).IsTrue();

        var dataModelJson = await newDirectory.Combine("Configs/NexusMods.DataModel.DataModelSettings.json").ReadAllTextAsync();
        await Assert.That(dataModelJson.Contains("Apocrypha/DataModel/MnemonicDB.rocksdb")).IsTrue();
        await Assert.That(dataModelJson.Contains("NexusMods.App")).IsFalse();

        var hashesJson = await newDirectory.Combine("Configs/NexusMods.Games.FileHashes.FileHashesServiceSettings.json").ReadAllTextAsync();
        await Assert.That(hashesJson.Contains("Apocrypha/FileHashesDatabase")).IsTrue();
        await Assert.That(hashesJson.Contains("github.com/Nexus-Mods/game-hashes")).IsTrue();
    }

    [Test]
    public async Task NothingToMigrate_IsANoOp()
    {
        using var sandbox = Sandbox.Create();

        var didWork = LegacyDataMigration.Run(sandbox.FileSystem, _ => { });

        await Assert.That(didWork).IsFalse();
        await Assert.That(sandbox.LocalAppData.Combine(ApplicationIdentity.DataDirectoryName).DirectoryExists()).IsFalse();
    }

    [Test]
    public async Task SecondRunIsANoOp()
    {
        using var sandbox = Sandbox.Create();
        WriteFile(sandbox.LocalAppData.Combine(ApplicationIdentity.LegacyDataDirectoryName).Combine("Configs/NexusMods.DataModel.DataModelSettings.json"), LegacyDataModelSettingsJson);

        var firstRun = LegacyDataMigration.Run(sandbox.FileSystem, _ => { });
        var secondRun = LegacyDataMigration.Run(sandbox.FileSystem, _ => { });

        await Assert.That(firstRun).IsTrue();
        await Assert.That(secondRun).IsFalse();
    }

    [Test]
    public async Task BothDirectoriesExist_LeavesTheOldOneAlone()
    {
        using var sandbox = Sandbox.Create();
        var oldDirectory = sandbox.LocalAppData.Combine(ApplicationIdentity.LegacyDataDirectoryName);
        var newDirectory = sandbox.LocalAppData.Combine(ApplicationIdentity.DataDirectoryName);
        WriteFile(oldDirectory.Combine("old-marker.txt"), "old");
        WriteFile(newDirectory.Combine("new-marker.txt"), "new");

        var messages = new List<string>();
        LegacyDataMigration.Run(sandbox.FileSystem, messages.Add);

        await Assert.That(oldDirectory.Combine("old-marker.txt").FileExists).IsTrue();
        await Assert.That(newDirectory.Combine("new-marker.txt").FileExists).IsTrue();
        await Assert.That(newDirectory.Combine("old-marker.txt").FileExists).IsFalse();
        await Assert.That(messages.Any(m => m.Contains("leaving the old directory untouched"))).IsTrue();
    }

    [Test]
    public async Task CrashBetweenMoveAndRewrite_SelfHealsOnNextRun()
    {
        using var sandbox = Sandbox.Create();
        // Simulates: previous run moved the directory, crashed before the config rewrite.
        var newDirectory = sandbox.LocalAppData.Combine(ApplicationIdentity.DataDirectoryName);
        WriteFile(newDirectory.Combine("Configs/NexusMods.DataModel.DataModelSettings.json"), LegacyDataModelSettingsJson);

        var didWork = LegacyDataMigration.Run(sandbox.FileSystem, _ => { });

        await Assert.That(didWork).IsTrue();
        var json = await newDirectory.Combine("Configs/NexusMods.DataModel.DataModelSettings.json").ReadAllTextAsync();
        await Assert.That(json.Contains("NexusMods.App")).IsFalse();
    }

    [Test]
    [Arguments(LegacyDataModelSettingsJson, "Apocrypha/DataModel/MnemonicDB.rocksdb")]
    [Arguments(LegacyLoggingSettingsJson, "Apocrypha/Logs/apocrypha.main.current.log")]
    [Arguments(LegacyCliSettingsJson, "/run/user/1000/Apocrypha-sync_file.sync")]
    public async Task RewriteLegacyFragments_RewritesKnownShapes(string input, string expectedFragment)
    {
        var rewritten = LegacyDataMigration.RewriteLegacyFragments(input);

        await Assert.That(rewritten.Contains(expectedFragment)).IsTrue();
        await Assert.That(rewritten.Contains("NexusMods.App/")).IsFalse();
    }

    [Test]
    [Arguments("""{"EnableTracking":false,"DeviceId":"019f3a6e-b92d-78b0-b42e-8c6580d58415"}""")]
    [Arguments("""{"GithubManifestUrl":"https://github.com/Nexus-Mods/game-hashes/releases/latest/download/manifest.json"}""")]
    public async Task RewriteLegacyFragments_LeavesUnrelatedContentAlone(string input)
    {
        var rewritten = LegacyDataMigration.RewriteLegacyFragments(input);

        await Assert.That(rewritten).IsEqualTo(input);
    }

    private static void WriteFile(AbsolutePath path, string contents)
    {
        path.Parent.CreateDirectory();
        path.WriteAllTextAsync(contents).GetAwaiter().GetResult();
    }

    /// <summary>
    /// A real-filesystem sandbox: every KnownPath base the migration can query is mapped
    /// into a fresh temp directory, so behavior is identical on every OS the tests run on.
    /// </summary>
    private sealed class Sandbox : IDisposable
    {
        public required IFileSystem FileSystem { get; init; }
        public required AbsolutePath Root { get; init; }

        /// <summary>The one base the migration visits on EVERY platform.</summary>
        public AbsolutePath LocalAppData => Root.Combine("local");

        public static Sandbox Create()
        {
            var realFileSystem = NexusMods.Paths.FileSystem.Shared;
            var root = realFileSystem
                .GetKnownPath(KnownPath.TempDirectory)
                .Combine($"r3-migration-tests-{Guid.NewGuid():N}");

            var knownPathMappings = new Dictionary<KnownPath, AbsolutePath>
            {
                { KnownPath.LocalApplicationDataDirectory, root.Combine("local") },
                { KnownPath.ApplicationDataDirectory, root.Combine("roaming") },
                { KnownPath.XDG_DATA_HOME, root.Combine("data") },
                { KnownPath.XDG_STATE_HOME, root.Combine("state") },
            };

            foreach (var directory in knownPathMappings.Values) directory.CreateDirectory();

            var fileSystem = realFileSystem.CreateOverlayFileSystem(new Dictionary<AbsolutePath, AbsolutePath>(), knownPathMappings);
            return new Sandbox { FileSystem = fileSystem, Root = root };
        }

        public void Dispose()
        {
            if (Root.DirectoryExists()) Root.DeleteDirectory(recursive: true);
        }
    }
}
