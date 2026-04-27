namespace Flowarr.Core.Integrations;

public sealed record RemoteTransferResult(
    string RemotePath,
    string LocalPath,
    long BytesTransferred,
    bool Skipped,
    string Message);

public interface IRemoteFileTransferClient
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
    Task<RemoteTransferResult> DownloadAsync(string remotePath, string localRootPath, CancellationToken cancellationToken = default);
}
