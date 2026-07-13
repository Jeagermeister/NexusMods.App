using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModIo.Models;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Networking.HttpDownloader;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Networking.ModIo;

/// <summary>
/// Download job for an exact mod.io modfile. Extends the generic HTTP download job (the
/// binary URL 302s to the mod.io CDN) and stamps mod.io identity onto the resulting
/// library item.
/// </summary>
public record ModIoDownloadJob : HttpDownloadJob, IModIoDownloadJob
{
    /// <inheritdoc/>
    public required ModIoFileMetadata.ReadOnly FileMetadata { get; init; }

    /// <inheritdoc/>
    public string DisplayName => FileMetadata.Mod.Name;

    /// <inheritdoc/>
    /// <remarks>
    /// TODO: resolve the app-side GameId from ModIoModMetadata.GameNameId via the game
    /// registry; the Downloads page tolerates unresolved games, so MVP matches Thunderstore.
    /// </remarks>
    public GameId GameId => default;

    /// <inheritdoc/>
    public EntityId MetadataEntityId => FileMetadata.Id;

    /// <inheritdoc/>
    /// <remarks>This job IS the HTTP transfer; there is no inner job.</remarks>
    public IJob? InnerJob => null;

    /// <inheritdoc/>
    public Optional<LibraryFile.ReadOnly> FindLibraryFile(IDb db)
    {
        var file = ModIoFileMetadata.Load(db, FileMetadata.Id);
        if (!file.IsValid()) return Optional<LibraryFile.ReadOnly>.None;

        foreach (var item in file.LibraryItems)
        {
            if (item.AsLibraryItem().TryGetAsLibraryFile(out var libraryFile))
                return Optional<LibraryFile.ReadOnly>.Create(libraryFile);
        }

        return Optional<LibraryFile.ReadOnly>.None;
    }

    /// <summary>
    /// Creates (and starts) a download job for the given modfile. The caller supplies a
    /// fresh binary URL (they expire — see DESIGN-modio.md §2).
    /// </summary>
    public static IJobTask<ModIoDownloadJob, AbsolutePath> Create(
        IServiceProvider provider,
        ModIoFileMetadata.ReadOnly fileMetadata,
        Uri binaryUri)
    {
        var monitor = provider.GetRequiredService<IJobMonitor>();
        var tempFileManager = provider.GetRequiredService<TemporaryFileManager>();

        var job = new ModIoDownloadJob
        {
            Logger = provider.GetRequiredService<ILogger<ModIoDownloadJob>>(),
            FileMetadata = fileMetadata,
            Uri = binaryUri,
            DownloadPageUri = fileMetadata.Mod.ProfileUri,
            Destination = tempFileManager.CreateFile(),
            Client = provider.GetRequiredService<HttpClient>(),
        };

        return monitor.Begin<ModIoDownloadJob, AbsolutePath>(job);
    }

    /// <inheritdoc/>
    public override ValueTask AddMetadata(ITransaction tx, LibraryFile.New libraryFile)
    {
        libraryFile.GetLibraryItem(tx).Name = FileMetadata.Mod.Name;
        libraryFile.FileName = RelativePath.FromUnsanitizedInput(FileMetadata.FileName);

        // Not using .New here because we can't use the LibraryItem Id and don't have the LibraryItem in this method
        tx.Add(libraryFile.Id, ModIoLibraryItem.FileId, FileMetadata.Id);

        _ = new DownloadedFile.New(tx, libraryFile.Id)
        {
            DownloadPageUri = DownloadPageUri,
            LibraryFile = libraryFile,
        };

        return ValueTask.CompletedTask;
    }
}
