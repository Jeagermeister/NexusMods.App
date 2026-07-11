using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.HttpDownloads;
using NexusMods.Abstractions.NexusModsLibrary;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Networking.HttpDownloader;
using NexusMods.Paths;
using DynamicData.Kernel;
using NexusMods.Abstractions.NexusModsLibrary.Models;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Library;

namespace NexusMods.Networking.NexusWebApi;

public class NexusModsDownloadJob : INexusModsDownloadJob, IJobDefinitionWithStart<NexusModsDownloadJob, AbsolutePath>
{
    public required ILogger Logger { private get; init; }
    public required IJobTask<IHttpDownloadJob, AbsolutePath> HttpDownloadJob { get; init; }
    public required NexusModsFileMetadata.ReadOnly FileMetadata { get; init; }
    public Optional<CollectionRevisionMetadata.ReadOnly> ParentRevision { get; init; }

    /// <inheritdoc/>
    public AbsolutePath Destination => HttpDownloadJob.JobDefinition.Destination;

    /// <inheritdoc/>
    public required GameId GameId { get; init; }

    /// <inheritdoc/>
    public string DisplayName => FileMetadata.Name;

    /// <inheritdoc/>
    public Uri DownloadPageUri => HttpDownloadJob.JobDefinition.DownloadPageUri;

    /// <inheritdoc/>
    public EntityId MetadataEntityId => FileMetadata.Id;

    /// <inheritdoc/>
    public IJob? InnerJob => HttpDownloadJob.Job;

    /// <inheritdoc/>
    public Optional<LibraryFile.ReadOnly> FindLibraryFile(IDb db)
    {
        var libraryItems = NexusModsLibraryItem.FindByFileMetadata(db, FileMetadata);
        if (libraryItems.Count == 0) return Optional<LibraryFile.ReadOnly>.None;

        var libraryItem = libraryItems.First().AsLibraryItem();
        return libraryItem.TryGetAsLibraryFile(out var libraryFile)
            ? Optional<LibraryFile.ReadOnly>.Create(libraryFile)
            : Optional<LibraryFile.ReadOnly>.None;
    }

    public static IJobTask<NexusModsDownloadJob, AbsolutePath> Create(
        IServiceProvider provider,
        IJobTask<HttpDownloadJob, AbsolutePath> httpDownloadJob,
        NexusModsFileMetadata.ReadOnly fileMetadata,
        Optional<CollectionRevisionMetadata.ReadOnly> parentRevision = default)
    {
        var monitor = provider.GetRequiredService<IJobMonitor>();

        // Map the Nexus game id to our own game identity for the source-agnostic downloads UI.
        var gameId = provider.GetServices<IGameData>()
            .FirstOrDefault(game => game.NexusModsGameId.HasValue && game.NexusModsGameId.Value.Equals(fileMetadata.Uid.GameId))
            ?.GameId ?? default;

        var job = new NexusModsDownloadJob
        {
            Logger = provider.GetRequiredService<ILogger<NexusModsDownloadJob>>(),
            HttpDownloadJob = httpDownloadJob,
            FileMetadata = fileMetadata,
            ParentRevision = parentRevision,
            GameId = gameId,
        };

        return monitor.Begin<NexusModsDownloadJob, AbsolutePath>(job);
    }

    public async ValueTask<AbsolutePath> StartAsync(IJobContext<NexusModsDownloadJob> context)
    {
        try
        {
            return await HttpDownloadJob;
        }
        catch (TaskCanceledException)
        {
            // Propagate cancellation so upstream jobs (e.g. AddDownloadJob) can abort follow-up actions.
            Logger.LogInformation("Download cancelled by user for file `{GameId}/{ModId}/{FileId}`", FileMetadata.Uid.GameId, FileMetadata.ModPage.Uid.ModId, FileMetadata.Uid.FileId);
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception while downloading file `{GameId}/{ModId}/{FileId}`", FileMetadata.Uid.GameId, FileMetadata.ModPage.Uid.ModId, FileMetadata.Uid.FileId);
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask AddMetadata(ITransaction tx, LibraryFile.New libraryFile)
    {
        libraryFile.GetLibraryItem(tx).Name = FileMetadata.Name;

        // Not using .New here because we can't use the LibraryItem Id and don't have the LibraryItem in this method
        tx.Add(libraryFile.Id, NexusModsLibraryItem.FileMetadataId, FileMetadata.Id);
        tx.Add(libraryFile.Id, NexusModsLibraryItem.ModPageMetadataId, FileMetadata.ModPage.Id);

        _ = new DownloadedFile.New(tx, libraryFile.Id)
        {
            DownloadPageUri = HttpDownloadJob.JobDefinition.DownloadPageUri,
            LibraryFile = libraryFile,
        };

        return ValueTask.CompletedTask;
    }

}
