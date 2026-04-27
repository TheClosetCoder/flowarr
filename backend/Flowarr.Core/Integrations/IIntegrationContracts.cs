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
    Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(CancellationToken cancellationToken = default);
}
