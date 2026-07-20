using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Options;
using Flowarr.Infrastructure.Services;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Flowarr.Infrastructure.Integrations;

public sealed class SftpSeedboxTransferClient(IConnectionSettingsService connectionSettingsService) : IRemoteFileTransferClient
{
    private SftpSeedboxConnectionSettings? _seedbox;
    private AutoDownloadConnectionSettings? _autoDownload;

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        return seedbox.Enabled &&
               !string.IsNullOrWhiteSpace(seedbox.Host) &&
               !string.IsNullOrWhiteSpace(seedbox.Username) &&
               (!string.IsNullOrWhiteSpace(seedbox.Password) ||
                !string.IsNullOrWhiteSpace(seedbox.PrivateKeyPath));
    }

    public async Task<RemoteTransferResult> DownloadAsync(string remotePath, string localRootPath, IProgress<long>? progress = null, Action<string>? onFileStarted = null, CancellationToken cancellationToken = default)
    {
        _seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        _autoDownload = await connectionSettingsService.GetAutoDownloadAsync(cancellationToken);

        if (!await IsConfiguredAsync(cancellationToken))
        {
            return new RemoteTransferResult(remotePath, localRootPath, 0, true, "SFTP seedbox is not configured.");
        }

        return await Task.Run(() =>
        {
            using var client = CreateClient();
            client.Connect();

            var normalizedRemotePath = NormalizeRemotePath(remotePath);
            if (!IsWithinConfiguredRoots(normalizedRemotePath))
            {
                return new RemoteTransferResult(normalizedRemotePath, localRootPath, 0, true, "Remote path is outside configured seedbox roots.");
            }

            if (!client.Exists(normalizedRemotePath))
            {
                return new RemoteTransferResult(normalizedRemotePath, localRootPath, 0, true, "Remote path does not exist.");
            }

            Directory.CreateDirectory(localRootPath);
            var attributes = client.GetAttributes(normalizedRemotePath);
            var bytes = attributes.IsDirectory
                ? DownloadDirectory(client, normalizedRemotePath, localRootPath, true, progress, onFileStarted, cancellationToken)
                : DownloadFile(client, normalizedRemotePath, localRootPath, progress, onFileStarted, cancellationToken);

            client.Disconnect();
            return new RemoteTransferResult(normalizedRemotePath, localRootPath, bytes, bytes == 0, "SFTP download completed.");
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RemoteFileEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        _seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);

        if (!await IsConfiguredAsync(cancellationToken))
        {
            return [];
        }

        return await Task.Run<IReadOnlyCollection<RemoteFileEntry>>(() =>
        {
            using var client = CreateClient();
            client.Connect();

            var normalizedRemotePath = NormalizeRemotePath(remotePath);
            if (!IsWithinConfiguredRoots(normalizedRemotePath) || !client.Exists(normalizedRemotePath))
            {
                client.Disconnect();
                return [];
            }

            var attributes = client.GetAttributes(normalizedRemotePath);
            if (!attributes.IsDirectory)
            {
                client.Disconnect();
                return [new RemoteFileEntry(Path.GetFileName(normalizedRemotePath), normalizedRemotePath, false, attributes.Size)];
            }

            var entries = client.ListDirectory(normalizedRemotePath)
                .Where(entry => entry.Name is not "." and not "..")
                .OrderBy(entry => entry.Name)
                .Select(entry => new RemoteFileEntry(
                    entry.Name,
                    entry.FullName,
                    entry.IsDirectory,
                    entry.IsDirectory ? GetRemoteSize(client, entry.FullName, cancellationToken) : entry.Attributes.Size))
                .ToList();

            client.Disconnect();
            return entries;
        }, cancellationToken);
    }

    public async Task<long> GetSizeAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        _seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        if (!await IsConfiguredAsync(cancellationToken))
        {
            return 0;
        }

        return await Task.Run(() =>
        {
            using var client = CreateClient();
            client.Connect();
            var normalizedRemotePath = NormalizeRemotePath(remotePath);
            var size = IsWithinConfiguredRoots(normalizedRemotePath) && client.Exists(normalizedRemotePath)
                ? GetRemoteSize(client, normalizedRemotePath, cancellationToken)
                : 0;
            client.Disconnect();
            return size;
        }, cancellationToken);
    }

    public async Task DeleteAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        _seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        if (!await IsConfiguredAsync(cancellationToken))
        {
            throw new InvalidOperationException("SFTP seedbox is not configured.");
        }

        await Task.Run(() =>
        {
            using var client = CreateClient();
            client.Connect();
            var normalizedRemotePath = NormalizeRemotePath(remotePath);
            if (!IsWithinConfiguredRoots(normalizedRemotePath))
            {
                throw new InvalidOperationException("Remote path is outside configured seedbox roots.");
            }

            if (!client.Exists(normalizedRemotePath))
            {
                client.Disconnect();
                return;
            }

            DeleteEntry(client, normalizedRemotePath, cancellationToken);
            client.Disconnect();
        }, cancellationToken);
    }

    private SftpClient CreateClient()
    {
        var seedbox = _seedbox ?? throw new InvalidOperationException("SFTP seedbox settings were not loaded.");
        if (!string.IsNullOrWhiteSpace(seedbox.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(seedbox.PrivateKeyPassphrase)
                ? new PrivateKeyFile(seedbox.PrivateKeyPath)
                : new PrivateKeyFile(seedbox.PrivateKeyPath, seedbox.PrivateKeyPassphrase);

            return new SftpClient(seedbox.Host, seedbox.Port, seedbox.Username, keyFile);
        }

        return new SftpClient(seedbox.Host, seedbox.Port, seedbox.Username, seedbox.Password);
    }

    private string NormalizeRemotePath(string remotePath)
    {
        var root = (_seedbox?.SeedboxRoots.FirstOrDefault(item => item.Enabled && item.Kind.Equals("Download", StringComparison.OrdinalIgnoreCase))?.Path
            ?? _seedbox?.SeedboxRoots.FirstOrDefault(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Path))?.Path
            ?? string.Empty).TrimEnd('/');
        var combined = string.IsNullOrWhiteSpace(root) || remotePath.StartsWith("/", StringComparison.Ordinal)
            ? remotePath
            : $"{root}/{remotePath.TrimStart('/')}";
        return RemotePathUtils.NormalizePosixPath(combined);
    }

    private bool IsWithinConfiguredRoots(string remotePath)
    {
        var roots = _seedbox?.SeedboxRoots
            .Where(root => root.Enabled)
            .Select(root => root.Path.TrimEnd('/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList() ?? [];

        return roots.Count == 0 ||
               roots.Any(root => remotePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                                 remotePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    private long DownloadDirectory(SftpClient client, string remotePath, string localRootPath, bool includeDirectoryName, IProgress<long>? progress, Action<string>? onFileStarted, CancellationToken cancellationToken)
    {
        var total = 0L;
        var localDirectory = includeDirectoryName
            ? Path.Combine(localRootPath, Path.GetFileName(remotePath.TrimEnd('/')))
            : localRootPath;
        Directory.CreateDirectory(localDirectory);

        foreach (var entry in client.ListDirectory(remotePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            total += entry.IsDirectory
                ? DownloadDirectory(client, entry.FullName, localDirectory, true, progress, onFileStarted, cancellationToken)
                : DownloadFile(client, entry.FullName, localDirectory, progress, onFileStarted, cancellationToken);
        }

        return total;
    }

    private long DownloadFile(SftpClient client, string remotePath, string localDirectory, IProgress<long>? progress, Action<string>? onFileStarted, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(remotePath);
        var autoDownload = _autoDownload ?? throw new InvalidOperationException("Auto-download settings were not loaded.");
        if (autoDownload.AllowedExtensions.Length > 0 &&
            !autoDownload.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        Directory.CreateDirectory(localDirectory);
        var localPath = Path.Combine(localDirectory, Path.GetFileName(remotePath));
        if (File.Exists(localPath))
        {
            var localSize = new FileInfo(localPath).Length;
            var remoteSize = client.GetAttributes(remotePath).Size;
            if (localSize == remoteSize && !autoDownload.OverwriteExisting)
            {
                // Already present and intact - not re-downloaded, but it still counts toward the
                // transfer's total so a requeue (new sibling files added since last run) doesn't
                // make BytesTransferred regress to just this run's delta instead of the true total.
                return localSize;
            }

            // A local file that doesn't match the remote size is a partial/corrupt leftover from
            // an interrupted download (e.g. a restart mid-transfer), not something to leave alone -
            // fall through and re-download it even though OverwriteExisting is off.
        }

        cancellationToken.ThrowIfCancellationRequested();
        onFileStarted?.Invoke(remotePath);
        using var output = File.Open(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var previousBytes = 0UL;
        client.DownloadFile(remotePath, output, bytesDownloaded =>
        {
            // SSH.NET's DownloadFile has no cancellation token of its own; throwing from inside
            // this periodically-invoked callback is what actually interrupts it mid-file.
            cancellationToken.ThrowIfCancellationRequested();

            // downloadCallback reports bytes downloaded so far for this file, not a delta.
            var delta = (long)(bytesDownloaded - previousBytes);
            previousBytes = bytesDownloaded;
            if (delta > 0)
            {
                progress?.Report(delta);
            }
        });
        return output.Length;
    }

    private long GetRemoteSize(SftpClient client, string remotePath, CancellationToken cancellationToken)
    {
        var attributes = client.GetAttributes(remotePath);
        if (!attributes.IsDirectory)
        {
            return attributes.Size;
        }

        var total = 0L;
        foreach (var entry in client.ListDirectory(remotePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            total += entry.IsDirectory ? GetRemoteSize(client, entry.FullName, cancellationToken) : entry.Attributes.Size;
        }

        return total;
    }

    private void DeleteEntry(SftpClient client, string remotePath, CancellationToken cancellationToken)
    {
        var attributes = client.GetAttributes(remotePath);
        if (!attributes.IsDirectory)
        {
            client.DeleteFile(remotePath);
            return;
        }

        foreach (var entry in client.ListDirectory(remotePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            DeleteEntry(client, entry.FullName, cancellationToken);
        }

        client.DeleteDirectory(remotePath);
    }
}
