using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Thunderstore;
using NexusMods.Abstractions.Thunderstore.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Networking.HttpDownloader;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Library;

namespace NexusMods.Networking.Thunderstore;

/// <summary>
/// Download job for an exact Thunderstore package version. Extends the generic HTTP download
/// job (the download endpoint 302s to the Thunderstore CDN) and stamps Thunderstore identity
/// onto the resulting library item.
/// </summary>
public record ThunderstoreDownloadJob : HttpDownloadJob, IThunderstoreDownloadJob
{
    /// <inheritdoc/>
    public required ThunderstoreVersionMetadata.ReadOnly VersionMetadata { get; init; }

    /// <inheritdoc/>
    public string DisplayName => $"{VersionMetadata.Package.Name} ({VersionMetadata.Package.PackageNamespace})";

    /// <inheritdoc/>
    /// <remarks>Thunderstore packages are global — the target game isn't known at download time.</remarks>
    public GameId GameId => default;

    /// <inheritdoc/>
    public EntityId MetadataEntityId => VersionMetadata.Id;

    /// <inheritdoc/>
    /// <remarks>This job IS the HTTP transfer; there is no inner job.</remarks>
    public IJob? InnerJob => null;

    /// <inheritdoc/>
    public Optional<LibraryFile.ReadOnly> FindLibraryFile(IDb db)
    {
        var version = ThunderstoreVersionMetadata.Load(db, VersionMetadata.Id);
        if (!version.IsValid()) return Optional<LibraryFile.ReadOnly>.None;

        foreach (var item in version.LibraryItems)
        {
            if (item.AsLibraryItem().TryGetAsLibraryFile(out var libraryFile))
                return Optional<LibraryFile.ReadOnly>.Create(libraryFile);
        }

        return Optional<LibraryFile.ReadOnly>.None;
    }

    /// <summary>
    /// Creates (and starts) a download job for the given package version.
    /// </summary>
    public static IJobTask<ThunderstoreDownloadJob, AbsolutePath> Create(
        IServiceProvider provider,
        ThunderstoreVersionMetadata.ReadOnly versionMetadata)
    {
        var monitor = provider.GetRequiredService<IJobMonitor>();
        var tempFileManager = provider.GetRequiredService<TemporaryFileManager>();

        PackageVersionRef.TryParse(versionMetadata.FullName, out var versionRef);

        var job = new ThunderstoreDownloadJob
        {
            Logger = provider.GetRequiredService<ILogger<ThunderstoreDownloadJob>>(),
            VersionMetadata = versionMetadata,
            Uri = ThunderstoreUrls.GetDownloadUri(versionRef),
            DownloadPageUri = versionMetadata.Package.PackageUri,
            Destination = tempFileManager.CreateFile(),
            Client = provider.GetRequiredService<HttpClient>(),
        };

        return monitor.Begin<ThunderstoreDownloadJob, AbsolutePath>(job);
    }

    /// <inheritdoc/>
    public override ValueTask AddMetadata(ITransaction tx, LibraryFile.New libraryFile)
    {
        var package = VersionMetadata.Package;
        libraryFile.GetLibraryItem(tx).Name = $"{package.Name} ({package.PackageNamespace})";
        libraryFile.FileName = RelativePath.FromUnsanitizedInput($"{VersionMetadata.FullName}.zip");

        // Not using .New here because we can't use the LibraryItem Id and don't have the LibraryItem in this method
        tx.Add(libraryFile.Id, ThunderstoreLibraryItem.VersionId, VersionMetadata.Id);

        _ = new DownloadedFile.New(tx, libraryFile.Id)
        {
            DownloadPageUri = DownloadPageUri,
            LibraryFile = libraryFile,
        };

        return ValueTask.CompletedTask;
    }
}
