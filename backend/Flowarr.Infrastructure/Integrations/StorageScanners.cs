using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Options;
using Flowarr.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Flowarr.Infrastructure.Integrations;

public sealed class LocalStorageScanner(
    IConnectionSettingsService connectionSettingsService,
    IOptions<IntegrationOptions> options) : IStorageScanner
{
    public async Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var autoDownload = await connectionSettingsService.GetAutoDownloadAsync(cancellationToken);
        var paths = autoDownload.LocalMediaPaths
            .Where(path => path.Enabled && !string.IsNullOrWhiteSpace(path.Path))
            .Select(path => new { Name = $"Local: {path.Name}", Path = path.Path })
            .Concat(autoDownload.TransferRules
                .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath))
                .Select(rule => new { Name = $"Local rule: {rule.Name}", Path = rule.LocalPath }))
            .Concat([new { Name = "Local media", Path = options.Value.Storage.LocalPath }])
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return paths.Select(item => BuildSnapshot(item.Name, item.Path)).ToList();
    }

    private static StorageSnapshot BuildSnapshot(string name, string path)
    {
        if (!Directory.Exists(path))
        {
            return new StorageSnapshot(name, StorageKind.Local, path, 0, 0);
        }

        var used = EnumerateLocalBytes(path);
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);

        return new StorageSnapshot(name, StorageKind.Local, path, used, drive?.AvailableFreeSpace ?? 0);
    }

    private static long EnumerateLocalBytes(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                })
                .Sum();
        }
        catch
        {
            return 0;
        }
    }
}

public sealed class SeedboxStorageScanner(
    IConnectionSettingsService connectionSettingsService,
    IOptions<IntegrationOptions> options) : IStorageScanner
{
    public async Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        var autoDownload = await connectionSettingsService.GetAutoDownloadAsync(cancellationToken);

        if (!IsConfigured(seedbox))
        {
            var fallback = options.Value.Storage.SeedboxPathOrEndpoint;
            return Directory.Exists(fallback)
                ? [BuildMountedSeedboxSnapshot("Seedbox mount", fallback)]
                : [new StorageSnapshot("Seedbox", StorageKind.Seedbox, fallback, 0, 0)];
        }

        var configuredRoots = seedbox.SeedboxRoots
            .Where(root => root.Enabled && !string.IsNullOrWhiteSpace(root.Path))
            .Select(root => new { Name = $"Seedbox {root.Kind}: {root.Name}", Path = root.Path })
            .ToList();
        var remotePaths = autoDownload.TransferRules
            .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.RemotePathPrefix))
            .Select(rule => new { Name = $"Seedbox rule: {rule.Name}", Path = NormalizeRemotePath(GetRootForRule(seedbox, rule), rule.RemotePathPrefix) })
            .Concat(configuredRoots)
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (remotePaths.Count == 0)
        {
            remotePaths.Add(new { Name = "Seedbox root", Path = seedbox.RemoteRootPath });
        }

        return await Task.Run<IReadOnlyCollection<StorageSnapshot>>(() =>
        {
            var snapshots = new List<StorageSnapshot>();
            using var client = CreateClient(seedbox);
            client.Connect();

            foreach (var remotePath in remotePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.Add(BuildSftpSnapshot(client, remotePath.Name, remotePath.Path, cancellationToken));
            }

            client.Disconnect();
            return snapshots;
        }, cancellationToken);
    }

    private static bool IsConfigured(SftpSeedboxConnectionSettings seedbox)
    {
        return seedbox.Enabled &&
               !string.IsNullOrWhiteSpace(seedbox.Host) &&
               !string.IsNullOrWhiteSpace(seedbox.Username) &&
               (!string.IsNullOrWhiteSpace(seedbox.Password) ||
                !string.IsNullOrWhiteSpace(seedbox.PrivateKeyPath));
    }

    private static SftpClient CreateClient(SftpSeedboxConnectionSettings seedbox)
    {
        if (!string.IsNullOrWhiteSpace(seedbox.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(seedbox.PrivateKeyPassphrase)
                ? new PrivateKeyFile(seedbox.PrivateKeyPath)
                : new PrivateKeyFile(seedbox.PrivateKeyPath, seedbox.PrivateKeyPassphrase);

            return new SftpClient(seedbox.Host, seedbox.Port, seedbox.Username, keyFile);
        }

        return new SftpClient(seedbox.Host, seedbox.Port, seedbox.Username, seedbox.Password);
    }

    private static string GetRootForRule(SftpSeedboxConnectionSettings seedbox, TransferRuleSettings rule)
    {
        return seedbox.SeedboxRoots
            .FirstOrDefault(root => root.Enabled &&
                                    (string.IsNullOrWhiteSpace(rule.SourceRootPath)
                                        ? root.Kind.Equals(rule.SourceRootKind, StringComparison.OrdinalIgnoreCase)
                                        : root.Path.Equals(rule.SourceRootPath, StringComparison.OrdinalIgnoreCase)))
            ?.Path ?? seedbox.RemoteRootPath;
    }

    private static StorageSnapshot BuildSftpSnapshot(
        SftpClient client,
        string name,
        string remotePath,
        CancellationToken cancellationToken)
    {
        if (!client.Exists(remotePath))
        {
            return new StorageSnapshot(name, StorageKind.Seedbox, remotePath, 0, 0);
        }

        var used = GetRemoteUsedBytes(client, remotePath, cancellationToken);
        var free = GetRemoteFreeBytes(client, remotePath);
        return new StorageSnapshot(name, StorageKind.Seedbox, remotePath, used, free);
    }

    private static long GetRemoteUsedBytes(SftpClient client, string remotePath, CancellationToken cancellationToken)
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

            try
            {
                total += entry.IsDirectory
                    ? GetRemoteUsedBytes(client, entry.FullName, cancellationToken)
                    : entry.Attributes.Size;
            }
            catch
            {
                // A file can disappear while the seedbox is active; skip transient scan failures.
            }
        }

        return total;
    }

    private static long GetRemoteFreeBytes(SftpClient client, string remotePath)
    {
        try
        {
            var status = client.GetStatus(remotePath);
            return (long)status.AvailableBlocks * (long)status.BlockSize;
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeRemotePath(string root, string remotePath)
    {
        if (!string.IsNullOrWhiteSpace(remotePath) && remotePath.StartsWith("/", StringComparison.Ordinal))
        {
            return remotePath;
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return remotePath;
        }

        return string.IsNullOrWhiteSpace(remotePath)
            ? root
            : $"{root.TrimEnd('/')}/{remotePath.TrimStart('/')}";
    }

    private static StorageSnapshot BuildMountedSeedboxSnapshot(string name, string path)
    {
        var used = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                try
                {
                    return new FileInfo(file).Length;
                }
                catch
                {
                    return 0L;
                }
            })
            .Sum();
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);

        return new StorageSnapshot(name, StorageKind.Seedbox, path, used, drive?.AvailableFreeSpace ?? 0);
    }
}
