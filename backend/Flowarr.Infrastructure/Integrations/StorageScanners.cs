using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Options;
using Flowarr.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Flowarr.Infrastructure.Integrations;

public sealed class LocalStorageScanner(
    IConnectionSettingsService connectionSettingsService,
    IOptions<IntegrationOptions> options) : IStorageScanner
{
    public async Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var autoDownload = await connectionSettingsService.GetAutoDownloadAsync(cancellationToken);
        var paths = autoDownload.LocalMediaPaths
            .Where(path => path.Enabled && !string.IsNullOrWhiteSpace(path.Path))
            .Select(path => new { Name = $"Local: {path.Name}", Path = path.Path, Category = StorageCategory.Monitored })
            .Concat(autoDownload.TransferRules
                .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath))
                .Select(rule => new { Name = $"Local rule: {rule.Name}", Path = rule.LocalPath, Category = StorageCategory.Rule }))
            .Concat([new { Name = "Local media", Path = options.Value.Storage.LocalPath, Category = StorageCategory.Monitored }])
            .GroupBy(item => (Path: item.Path.ToLowerInvariant(), item.Category))
            .Select(group => group.First())
            .ToList();

        var results = new List<StorageSnapshot>();
        foreach (var item in paths)
        {
            if (onProgress is not null)
            {
                await onProgress($"Scanning local folder: {item.Name}...");
            }

            results.Add(BuildSnapshot(item.Name, item.Path, item.Category));
        }

        return results;
    }

    private static StorageSnapshot BuildSnapshot(string name, string path, StorageCategory category)
    {
        if (!Directory.Exists(path))
        {
            return new StorageSnapshot(name, StorageKind.Local, path, 0, 0, category);
        }

        var used = EnumerateLocalBytes(path);
        return new StorageSnapshot(name, StorageKind.Local, path, used, MountLookup.GetAvailableFreeSpace(path), category);
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
    IOptions<IntegrationOptions> options,
    IMemoryCache cache) : IStorageScanner
{
    private const string AccountUsedBytesCacheKey = "seedbox-account-used-bytes";
    private static readonly TimeSpan AccountUsageCacheDuration = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        var autoDownload = await connectionSettingsService.GetAutoDownloadAsync(cancellationToken);

        if (!IsConfigured(seedbox))
        {
            var fallback = options.Value.Storage.SeedboxPathOrEndpoint;
            return Directory.Exists(fallback)
                ? [BuildMountedSeedboxSnapshot("Seedbox mount", fallback)]
                : [new StorageSnapshot("Seedbox", StorageKind.Seedbox, fallback, 0, 0, StorageCategory.Monitored)];
        }

        var configuredRoots = seedbox.SeedboxRoots
            .Where(root => root.Enabled && !string.IsNullOrWhiteSpace(root.Path))
            .Select(root => new { Name = $"Seedbox {root.Kind}: {root.Name}", Path = root.Path, Category = StorageCategory.Monitored })
            .ToList();
        var remotePaths = autoDownload.TransferRules
            .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.RemotePathPrefix))
            .Select(rule => new { Name = $"Seedbox rule: {rule.Name}", Path = NormalizeRemotePath(GetRootForRule(seedbox, rule), rule.RemotePathPrefix), Category = StorageCategory.Rule })
            .Concat(configuredRoots)
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => (Path: item.Path.ToLowerInvariant(), item.Category))
            .Select(group => group.First())
            .ToList();

        return await Task.Run<IReadOnlyCollection<StorageSnapshot>>(async () =>
        {
            var snapshots = new List<StorageSnapshot>();
            using var client = CreateClient(seedbox);
            client.Connect();

            if (onProgress is not null && seedbox.AccountQuotaBytes > 0)
            {
                await onProgress("Scanning seedbox account usage against your configured quota (this can take a while for large accounts)...");
            }

            var accountFreeBytes = GetAccountFreeBytes(client, seedbox, cancellationToken);

            foreach (var remotePath in remotePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (onProgress is not null)
                {
                    await onProgress($"Scanning seedbox root: {remotePath.Name} ({remotePath.Path})...");
                }

                snapshots.Add(BuildSftpSnapshot(client, remotePath.Name, remotePath.Path, remotePath.Category, accountFreeBytes, cancellationToken));
            }

            client.Disconnect();
            return snapshots;
        }, cancellationToken);
    }

    /// <summary>
    /// SFTP's statvfs response reports the whole shared host disk's free space, not the user's
    /// actual account quota. When a quota is configured, this computes (and caches, since it
    /// requires a full recursive scan of the account root) a single account-wide free-space
    /// figure to use across every seedbox entry instead - the same shared-pool approach used for
    /// local bind-mounted disks in MountLookup.
    /// </summary>
    private long? GetAccountFreeBytes(SftpClient client, SftpSeedboxConnectionSettings seedbox, CancellationToken cancellationToken)
    {
        var root = PrimaryRootPath(seedbox);
        if (seedbox.AccountQuotaBytes <= 0 || string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        if (!cache.TryGetValue(AccountUsedBytesCacheKey, out long accountUsedBytes))
        {
            if (!client.Exists(root))
            {
                return null;
            }

            accountUsedBytes = GetRemoteUsedBytes(client, root, cancellationToken);
            cache.Set(AccountUsedBytesCacheKey, accountUsedBytes, AccountUsageCacheDuration);
        }

        return Math.Max(0, seedbox.AccountQuotaBytes - accountUsedBytes);
    }

    private static string? PrimaryRootPath(SftpSeedboxConnectionSettings seedbox)
    {
        return seedbox.SeedboxRoots.FirstOrDefault(root => root.Enabled && !string.IsNullOrWhiteSpace(root.Path))?.Path;
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
            ?.Path ?? PrimaryRootPath(seedbox) ?? string.Empty;
    }

    private static StorageSnapshot BuildSftpSnapshot(
        SftpClient client,
        string name,
        string remotePath,
        StorageCategory category,
        long? accountFreeBytesOverride,
        CancellationToken cancellationToken)
    {
        if (!client.Exists(remotePath))
        {
            return new StorageSnapshot(name, StorageKind.Seedbox, remotePath, 0, 0, category);
        }

        var used = GetRemoteUsedBytes(client, remotePath, cancellationToken);
        var free = accountFreeBytesOverride ?? GetRemoteFreeBytes(client, remotePath);
        return new StorageSnapshot(name, StorageKind.Seedbox, remotePath, used, free, category);
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

        return new StorageSnapshot(name, StorageKind.Seedbox, path, used, MountLookup.GetAvailableFreeSpace(path), StorageCategory.Monitored);
    }
}

/// <summary>
/// Path.GetPathRoot always resolves to "/" for any absolute Unix path, so a naive
/// new DriveInfo(Path.GetPathRoot(path)) always reports the container's root filesystem
/// free space, not the free space of whatever bind mount actually backs the given path
/// (e.g. /media). This finds the most specific mounted filesystem containing the path instead.
/// </summary>
internal static class MountLookup
{
    public static long GetAvailableFreeSpace(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path).TrimEnd('/');
        }
        catch
        {
            return 0;
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return 0;
        }

        var candidates = drives
            .Where(drive =>
            {
                var mount = drive.Name.TrimEnd('/');
                return fullPath.Equals(mount, StringComparison.Ordinal) ||
                       fullPath.StartsWith(mount + "/", StringComparison.Ordinal) ||
                       mount.Length == 0;
            })
            .OrderByDescending(drive => drive.Name.Length);

        foreach (var drive in candidates)
        {
            try
            {
                return drive.AvailableFreeSpace;
            }
            catch
            {
                // Some virtual filesystems (proc, sysfs, etc.) aren't queryable; try the next-best match.
            }
        }

        return 0;
    }
}
