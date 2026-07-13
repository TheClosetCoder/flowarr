using Flowarr.Core.Entities;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Infrastructure.Services;

public interface IStorageSyncService
{
    Task SyncAsync(IEnumerable<IStorageScanner> scanners, CancellationToken cancellationToken = default);
}

public sealed class StorageSyncService(FlowarrDbContext dbContext) : IStorageSyncService
{
    public async Task SyncAsync(IEnumerable<IStorageScanner> scanners, CancellationToken cancellationToken = default)
    {
        foreach (var scanner in scanners)
        {
            var snapshots = await scanner.ScanAsync(cancellationToken);
            foreach (var snapshot in snapshots)
            {
                var location = await dbContext.StorageLocations
                    .FirstOrDefaultAsync(item => item.Kind == snapshot.Kind && item.Name == snapshot.Name, cancellationToken);

                if (location is null)
                {
                    location = new StorageLocation
                    {
                        Id = Guid.NewGuid(),
                        Kind = snapshot.Kind,
                        Name = snapshot.Name
                    };
                    dbContext.StorageLocations.Add(location);
                }

                location.Category = snapshot.Category;
                location.PathOrEndpoint = snapshot.PathOrEndpoint;
                location.UsedBytes = snapshot.UsedBytes;
                location.FreeBytes = snapshot.FreeBytes;
                location.LastScannedAt = DateTimeOffset.UtcNow;
            }

            // Remove rows this scanner no longer produces (renamed/removed roots and rules), so
            // stale entries don't linger forever showing whatever value they had at the time.
            foreach (var kindGroup in snapshots.GroupBy(snapshot => snapshot.Kind))
            {
                var currentNames = kindGroup.Select(snapshot => snapshot.Name).ToHashSet(StringComparer.Ordinal);
                var staleLocations = await dbContext.StorageLocations
                    .Where(item => item.Kind == kindGroup.Key && !currentNames.Contains(item.Name))
                    .ToListAsync(cancellationToken);
                dbContext.StorageLocations.RemoveRange(staleLocations);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
