using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Flowarr.Infrastructure.Integrations;

public sealed class SftpSeedboxTransferClient(IOptions<IntegrationOptions> options) : IRemoteFileTransferClient
{
    private readonly IntegrationOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.SftpSeedbox.Host) &&
        !string.IsNullOrWhiteSpace(_options.SftpSeedbox.Username) &&
        (!string.IsNullOrWhiteSpace(_options.SftpSeedbox.Password) ||
         !string.IsNullOrWhiteSpace(_options.SftpSeedbox.PrivateKeyPath));

    public Task<RemoteTransferResult> DownloadAsync(string remotePath, string localRootPath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(new RemoteTransferResult(remotePath, localRootPath, 0, true, "SFTP seedbox is not configured."));
        }

        return Task.Run(() =>
        {
            using var client = CreateClient();
            client.Connect();

            var normalizedRemotePath = NormalizeRemotePath(remotePath);
            if (!client.Exists(normalizedRemotePath))
            {
                return new RemoteTransferResult(normalizedRemotePath, localRootPath, 0, true, "Remote path does not exist.");
            }

            Directory.CreateDirectory(localRootPath);
            var attributes = client.GetAttributes(normalizedRemotePath);
            var bytes = attributes.IsDirectory
                ? DownloadDirectory(client, normalizedRemotePath, localRootPath, cancellationToken)
                : DownloadFile(client, normalizedRemotePath, localRootPath, cancellationToken);

            client.Disconnect();
            return new RemoteTransferResult(normalizedRemotePath, localRootPath, bytes, bytes == 0, "SFTP download completed.");
        }, cancellationToken);
    }

    private SftpClient CreateClient()
    {
        var seedbox = _options.SftpSeedbox;
        if (!string.IsNullOrWhiteSpace(seedbox.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(seedbox.Password)
                ? new PrivateKeyFile(seedbox.PrivateKeyPath)
                : new PrivateKeyFile(seedbox.PrivateKeyPath, seedbox.Password);

            return new SftpClient(seedbox.Host, seedbox.Port, seedbox.Username, keyFile);
        }

        return new SftpClient(seedbox.Host, seedbox.Port, seedbox.Username, seedbox.Password);
    }

    private string NormalizeRemotePath(string remotePath)
    {
        var root = _options.SftpSeedbox.RemoteRootPath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(root) || remotePath.StartsWith("/", StringComparison.Ordinal))
        {
            return remotePath;
        }

        return $"{root}/{remotePath.TrimStart('/')}";
    }

    private long DownloadDirectory(SftpClient client, string remotePath, string localRootPath, CancellationToken cancellationToken)
    {
        var total = 0L;
        var localDirectory = Path.Combine(localRootPath, Path.GetFileName(remotePath.TrimEnd('/')));
        Directory.CreateDirectory(localDirectory);

        foreach (var entry in client.ListDirectory(remotePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            total += entry.IsDirectory
                ? DownloadDirectory(client, entry.FullName, localDirectory, cancellationToken)
                : DownloadFile(client, entry.FullName, localDirectory, cancellationToken);
        }

        return total;
    }

    private long DownloadFile(SftpClient client, string remotePath, string localDirectory, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(remotePath);
        if (_options.AutoDownload.AllowedExtensions.Length > 0 &&
            !_options.AutoDownload.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        Directory.CreateDirectory(localDirectory);
        var localPath = Path.Combine(localDirectory, Path.GetFileName(remotePath));
        if (File.Exists(localPath) && !_options.AutoDownload.OverwriteExisting)
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var output = File.Open(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
        client.DownloadFile(remotePath, output);
        return output.Length;
    }
}
