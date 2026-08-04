using Microsoft.Extensions.Logging.Abstractions;
using Apocrypha.Backend.Process;
using NexusMods.Paths;
using TUnit.Assertions;

namespace Apocrypha.Backend.Tests.Process;

public class ProcessRunnerRetentionTests
{
    [Test]
    public async Task CleanupOldLogs_DeletesOnlyExpiredLogFiles()
    {
        using var temporaryFileManager = new TemporaryFileManager(FileSystem.Shared);
        await using var folder = temporaryFileManager.CreateFolder();

        var expiredStdOut = folder.Path.Combine("tool-a.stdout.log");
        var expiredStdErr = folder.Path.Combine("tool-a.stderr.log");
        var freshStdOut = folder.Path.Combine("tool-b.stdout.log");
        var expiredButNotALog = folder.Path.Combine("notes.txt");

        foreach (var file in new[] { expiredStdOut, expiredStdErr, freshStdOut, expiredButNotALog })
        {
            await file.WriteAllTextAsync("contents");
        }

        var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(7);
        foreach (var file in new[] { expiredStdOut, expiredStdErr, expiredButNotALog })
        {
            File.SetLastWriteTimeUtc(file.ToString(), cutoffUtc - TimeSpan.FromDays(1));
        }

        var deletedCount = ProcessRunner.CleanupOldLogs(folder.Path, cutoffUtc, NullLogger.Instance);

        await Assert.That(deletedCount).IsEqualTo(2);
        await Assert.That(expiredStdOut.FileExists).IsFalse();
        await Assert.That(expiredStdErr.FileExists).IsFalse();
        await Assert.That(freshStdOut.FileExists).IsTrue();
        await Assert.That(expiredButNotALog.FileExists).IsTrue();
    }

    [Test]
    public async Task CleanupOldLogs_ReturnsZeroForMissingFolder()
    {
        using var temporaryFileManager = new TemporaryFileManager(FileSystem.Shared);
        await using var folder = temporaryFileManager.CreateFolder();

        var missingFolder = folder.Path.Combine("does-not-exist");
        var deletedCount = ProcessRunner.CleanupOldLogs(missingFolder, DateTime.UtcNow, NullLogger.Instance);

        await Assert.That(deletedCount).IsEqualTo(0);
    }
}
