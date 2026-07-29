using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Backend.Process;
using NexusMods.Paths;
using TUnit.Assertions;

namespace Apocrypha.Backend.Tests.Process;

/// <summary>
/// The process-log folder grew without bound until the retention sweep landed (10,452 files on a
/// real install). These pin the two things that make the sweep safe to run at startup: it removes
/// only what it is meant to, and it leaves everything else in the folder alone.
/// </summary>
public class ProcessLogRetentionTests
{
    private static AbsolutePath CreateTempFolder()
    {
        var folder = NexusMods.Paths.FileSystem.Shared
            .GetKnownPath(KnownPath.TempDirectory)
            .Combine($"Apocrypha.ProcessLogRetentionTests-{Guid.NewGuid()}");
        folder.CreateDirectory();
        return folder;
    }

    private static async Task<AbsolutePath> WriteLog(AbsolutePath folder, string fileName, TimeSpan age)
    {
        var path = folder.Combine(fileName);
        await path.WriteAllTextAsync("log contents");
        File.SetLastWriteTimeUtc(path.ToString(), DateTime.UtcNow - age);
        return path;
    }

    [Test]
    public async Task Sweep_DeletesOnlyStaleProcessLogs()
    {
        var folder = CreateTempFolder();
        try
        {
            var staleOut = await WriteLog(folder, "game-aaa.stdout.log", TimeSpan.FromDays(30));
            var staleErr = await WriteLog(folder, "game-aaa.stderr.log", TimeSpan.FromDays(30));
            var freshOut = await WriteLog(folder, "game-bbb.stdout.log", TimeSpan.FromHours(1));

            var (deleted, failed) = ProcessRunner.SweepOldProcessLogs(folder, TimeSpan.FromDays(7), NullLogger.Instance);

            await Assert.That(deleted).IsEqualTo(2);
            await Assert.That(failed).IsEqualTo(0);
            await Assert.That(staleOut.FileExists).IsFalse();
            await Assert.That(staleErr.FileExists).IsFalse();
            await Assert.That(freshOut.FileExists).IsTrue();
        }
        finally
        {
            folder.DeleteDirectory(recursive: true);
        }
    }

    [Test]
    public async Task Sweep_LeavesUnrelatedFilesAlone()
    {
        var folder = CreateTempFolder();
        try
        {
            // Old enough to be swept, but none of these are process logs. The sweep shares a tree
            // with the app's own rolling logs, so a broader pattern would eat them.
            var appLog = await WriteLog(folder, "apocrypha.main.current.log", TimeSpan.FromDays(30));
            var archived = await WriteLog(folder, "apocrypha.main.01.log", TimeSpan.FromDays(30));
            var notALog = await WriteLog(folder, "game-ccc.stdout.txt", TimeSpan.FromDays(30));

            var (deleted, failed) = ProcessRunner.SweepOldProcessLogs(folder, TimeSpan.FromDays(7), NullLogger.Instance);

            await Assert.That(deleted).IsEqualTo(0);
            await Assert.That(failed).IsEqualTo(0);
            await Assert.That(appLog.FileExists).IsTrue();
            await Assert.That(archived.FileExists).IsTrue();
            await Assert.That(notALog.FileExists).IsTrue();
        }
        finally
        {
            folder.DeleteDirectory(recursive: true);
        }
    }

    [Test]
    public async Task Sweep_IsDisabledByNonPositiveRetention()
    {
        var folder = CreateTempFolder();
        try
        {
            var ancient = await WriteLog(folder, "game-ddd.stdout.log", TimeSpan.FromDays(365));

            var (deleted, _) = ProcessRunner.SweepOldProcessLogs(folder, TimeSpan.Zero, NullLogger.Instance);

            await Assert.That(deleted).IsEqualTo(0);
            await Assert.That(ancient.FileExists).IsTrue();
        }
        finally
        {
            folder.DeleteDirectory(recursive: true);
        }
    }
}
