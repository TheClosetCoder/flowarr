namespace Flowarr.Core.Integrations;

public interface IDownloadClient
{
    Task<IReadOnlyCollection<DownloadSnapshot>> GetDownloadsAsync(CancellationToken cancellationToken = default);
}

public interface IMediaCatalogClient
{
    Task<IReadOnlyCollection<MediaItemSnapshot>> GetMediaItemsAsync(CancellationToken cancellationToken = default);
}

public interface IStorageScanner
{
    Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default);
}
