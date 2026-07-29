using System.Diagnostics;
using CliWrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.CrossPlatform;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Backend.Process;

internal class ProcessRunner : IProcessRunner
{
    private readonly ILogger _logger;
    private readonly IFileSystem _fileSystem;
    private readonly AbsolutePath _processLogsFolder;

    public ProcessRunner(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILogger<ProcessRunner>>();
        _fileSystem = serviceProvider.GetRequiredService<IFileSystem>();

        _processLogsFolder = LoggingSettings.GetLogBaseFolder(_fileSystem.OS, _fileSystem).Combine("ProcessLogs");
        _logger.LogInformation("Using process log folder at {Path}", _processLogsFolder);

        _processLogsFolder.CreateDirectory();

        // Every logged run leaves a `{name}-{guid}.stdout/.stderr.log` pair behind and nothing
        // ever removed them, so the folder grew without bound (10,452 files on one real install).
        // The retention span has existed as a setting since the fork but was never consumed.
        // Everything here happens off the construction path on purpose. Resolving the settings
        // manager while the host is still wiring itself up pulls settings initialisation into
        // whatever is being constructed alongside us -- janitorial work must not perturb startup
        // ordering for the sake of deleting old files.
        var logsFolder = _processLogsFolder;
        var logger = _logger;
        Task.Run(() =>
            {
                var retentionSpan = serviceProvider.GetRequiredService<ISettingsManager>().Get<LoggingSettings>().ProcessLogRetentionSpan;
                SweepOldProcessLogs(logsFolder, retentionSpan, logger);
            })
            .FireAndForget(_logger, cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Deletes process logs older than <paramref name="retentionSpan"/>, returning how many were
    /// removed and how many resisted. Only the two suffixes this class writes, and only in its own
    /// folder -- never recursive, so nothing else sharing the Logs tree can be caught by it.
    /// A non-positive span disables the sweep.
    /// </summary>
    internal static (int Deleted, int Failed) SweepOldProcessLogs(AbsolutePath processLogsFolder, TimeSpan retentionSpan, ILogger logger)
    {
        if (retentionSpan <= TimeSpan.Zero) return (0, 0);
        if (!processLogsFolder.DirectoryExists()) return (0, 0);

        var cutoff = DateTime.UtcNow - retentionSpan;
        var deleted = 0;
        var failed = 0;

        foreach (var pattern in ProcessLogPatterns)
        {
            foreach (var file in processLogsFolder.EnumerateFiles(pattern, recursive: false))
            {
                try
                {
                    if (file.FileInfo.LastWriteTimeUtc >= cutoff) continue;
                    file.Delete();
                    deleted++;
                }
                catch (Exception e)
                {
                    // A log we cannot delete is not worth failing startup over; count it and move on.
                    failed++;
                    logger.LogDebug(e, "Failed to delete stale process log {Path}", file);
                }
            }
        }

        if (deleted > 0 || failed > 0)
            logger.LogInformation("Swept {Deleted} process log(s) older than {Retention} ({Failed} could not be deleted)", deleted, retentionSpan, failed);

        return (deleted, failed);
    }

    private static readonly string[] ProcessLogPatterns = ["*.stdout.log", "*.stderr.log"];

    private string GetFileName(Command command)
    {
        return PathHelpers.IsRooted(command.TargetFilePath)
            ? _fileSystem.FromUnsanitizedFullPath(command.TargetFilePath).FileName
            : RelativePath.FromUnsanitizedInput(command.TargetFilePath).FileName.ToString();
    }

    private static string GetLogFileName(string fileName)
    {
        // TODO: consider smaller IDs for shorted file names
        var id = Guid.NewGuid();
        return $"{fileName}-{id:D}";
    }

    public void Run(Command command, bool logOutput)
    {
        var task = ExecuteCommand(command, logOutput, cancellationToken: CancellationToken.None);
        task.FireAndForget(_logger, cancellationToken: CancellationToken.None);
    }

    public Task<CommandResult> RunAsync(Command command, bool logOutput, CancellationToken cancellationToken = default)
    {
        command = SetupLogging(command, logOutput);
        return ExecuteCommand(command, logOutput, cancellationToken);
    }

    private async Task<CommandResult> ExecuteCommand(Command command, bool logOutput, CancellationToken cancellationToken)
    {
        if (logOutput) _logger.LogDebug("Starting command `{Command}`", command.ToString());

        var sw = Stopwatch.StartNew();
        var result = await command.ExecuteAsync(cancellationToken: cancellationToken);
        sw.Stop();

        if (!logOutput) return result;
        _logger.LogDebug("Command `{Command}` finished after {RunTime} seconds with exit Code {ExitCode}", command.ToString(), result.RunTime.TotalSeconds, result.ExitCode);
        return result;
    }

    private Command SetupLogging(Command command, bool logOutput)
    {
        var stdInPipe = command.StandardInputPipe == PipeSource.Null ? PipeSource.Null : command.StandardInputPipe;

        if (!logOutput)
        {
            // We require a non-null pipe here, for more details, see:
            // https://github.com/Nexus-Mods/NexusMods.App/issues/1905#issuecomment-2302503110
            // https://github.com/Nexus-Mods/NexusMods.App/issues/1905#issuecomment-2302486535
            command = command.WithStandardInputPipe(stdInPipe);
            if (command.StandardOutputPipe == PipeTarget.Null) command = command.WithStandardOutputPipe(PipeTarget.ToStream(Stream.Null));
            // Checking the ERROR pipe here: the old copy-paste re-check of the output pipe
            // was always false after the line above, so stderr kept the null sentinel and
            // the pipe-handling issue the comment above cites persisted for stderr.
            if (command.StandardErrorPipe == PipeTarget.Null) command = command.WithStandardErrorPipe(PipeTarget.ToStream(Stream.Null));
            return command;
        }

        var fileName = GetFileName(command);
        var logFileName = GetLogFileName(fileName);
        var stdOutFilePath = _processLogsFolder.Combine(logFileName + ".stdout.log");
        var stdErrFilePath = _processLogsFolder.Combine(logFileName + ".stderr.log");

        var stdOutPipe = PipeTarget.Create(async (stdOut, cancellationToken) =>
        {
            await using var fileStream = stdOutFilePath.Open(FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            await stdOut.CopyToAsync(fileStream, cancellationToken: cancellationToken);
        });

        var stdErrPipe = PipeTarget.Create(async (stdOut, cancellationToken) =>
        {
            await using var fileStream = stdErrFilePath.Open(FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            await stdOut.CopyToAsync(fileStream, cancellationToken: cancellationToken);
        });

        var mergedStdOutPipe = command.StandardOutputPipe == PipeTarget.Null ? stdOutPipe : PipeTarget.Merge(command.StandardOutputPipe, stdOutPipe);
        var mergedStdErrPipe = command.StandardErrorPipe == PipeTarget.Null ? stdErrPipe : PipeTarget.Merge(command.StandardErrorPipe, stdErrPipe);

        _logger.LogInformation("Setup process logs {StdOutLogPath} and {StdErrLogPath}", stdOutFilePath, stdErrFilePath);
        return command
            .WithStandardInputPipe(stdInPipe)
            .WithStandardOutputPipe(mergedStdOutPipe)
            .WithStandardErrorPipe(mergedStdErrPipe);
    }

    public Task RunAsync(System.Diagnostics.Process process, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource();

        process.EnableRaisingEvents = true;
        var hasExited = false;

        process.Exited += (_, _) =>
        {
            hasExited = true;
            tcs.SetResult();
            process.Dispose();
        };
        
        cancellationToken.Register(() =>
        {
            if (hasExited) return;
            try
            {
                _logger.LogInformation("Killing process `{Process}`", process.StartInfo.FileName);
                process.Kill();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to kill process `{Process}`", process.StartInfo.FileName);
                tcs.SetException(e);
            }
        });

        try
        {
            _logger.LogInformation("Executing process `{Process}`", process.StartInfo.FileName);
            process.Start();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to start process `{Process}`", process.StartInfo.FileName);
            tcs.SetException(e);
        }

        return tcs.Task; 
    }
}
