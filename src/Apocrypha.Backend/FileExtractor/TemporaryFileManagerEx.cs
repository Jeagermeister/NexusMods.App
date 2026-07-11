using Apocrypha.Abstractions.FileExtractor;
using Apocrypha.Sdk.Settings;
using NexusMods.Paths;
using Apocrypha.Sdk.FileExtractor;

namespace Apocrypha.FileExtractor;

/// <summary>
/// Variation of <see cref="TemporaryFileManager"/> with support for injecting settings via DI.
/// </summary>
internal class TemporaryFileManagerEx : TemporaryFileManager
{
    public TemporaryFileManagerEx(IFileSystem fileSystem, ISettingsManager settingsManager)
        : base(
            fileSystem,
            basePath: settingsManager.Get<FileExtractorSettings>().TempFolderLocation.ToPath(fileSystem)
        ) { }
}
