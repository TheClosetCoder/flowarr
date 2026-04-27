using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Flowarr.Infrastructure.Integrations;

public sealed class LocalStorageScanner(IOptions<IntegrationOptions> options) : IStorageScanner
{
    public Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var path = options.Value.Storage.LocalPath;
        var snapshot = Directory.Exists(path)
            ? BuildSnapshot("Local media", StorageKind.Local, path)
            : new StorageSnapshot("Local media", StorageKind.Local, path, 0, 0);

        return Task.FromResult<IReadOnlyCollection<StorageSnapshot>>([snapshot]);
    }

    private static StorageSnapshot BuildSnapshot(string name, StorageKind kind, string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
        var used = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file).Length)
            .Sum();

        return new StorageSnapshot(name, kind, path, used, drive?.AvailableFreeSpace ?? 0);
    }
}

public sealed class SeedboxStorageScanner(IOptions<IntegrationOptions> options) : IStorageScanner
{
    public Task<IReadOnlyCollection<StorageSnapshot>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var target = options.Value.Storage.SeedboxPathOrEndpoint;
        var snapshot = Directory.Exists(target)
            ? BuildSnapshot(target)
            : new StorageSnapshot("Seedbox", StorageKind.Seedbox, target, 7_516_192_768, 42_949_672_960);

        return Task.FromResult<IReadOnlyCollection<StorageSnapshot>>([snapshot]);
    }

    private static StorageSnapshot BuildSnapshot(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
        var used = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file).Length)
            .Sum();

        return new StorageSnapshot("Seedbox", StorageKind.Seedbox, path, used, drive?.AvailableFreeSpace ?? 0);
    }
}
