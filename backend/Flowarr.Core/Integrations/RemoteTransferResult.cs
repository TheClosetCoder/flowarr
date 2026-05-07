namespace Flowarr.Core.Integrations;

public sealed record RemoteTransferResult(
    string RemotePath,
    string LocalPath,
    long BytesTransferred,
    bool Skipped,
    string Message);

public sealed record RemoteFileEntry(
    string Name,
    string RemotePath,
    bool IsDirectory,
    long SizeBytes);

public interface IRemoteFileTransferClient
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RemoteFileEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default);
    Task<RemoteTransferResult> DownloadAsync(string remotePath, string localRootPath, CancellationToken cancellationToken = default);
}
