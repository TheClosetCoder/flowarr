using Flowarr.Core.Entities;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/storage")]
public sealed class StorageController(
    FlowarrDbContext dbContext,
    IEnumerable<IStorageScanner> storageScanners) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var storage = await dbContext.StorageLocations
            .OrderBy(location => location.Kind)
            .ThenBy(location => location.Name)
            .ToListAsync(cancellationToken);

        return Ok(storage);
    }

    [HttpPost("scan")]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        foreach (var scanner in storageScanners)
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

                location.PathOrEndpoint = snapshot.PathOrEndpoint;
                location.UsedBytes = snapshot.UsedBytes;
                location.FreeBytes = snapshot.FreeBytes;
                location.LastScannedAt = DateTimeOffset.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await Get(cancellationToken);
    }
}
