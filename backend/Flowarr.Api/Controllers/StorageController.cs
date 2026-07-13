using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/storage")]
public sealed class StorageController(
    FlowarrDbContext dbContext,
    IEnumerable<IStorageScanner> storageScanners,
    IStorageSyncService storageSyncService) : ControllerBase
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
        await storageSyncService.SyncAsync(storageScanners, cancellationToken);
        return await Get(cancellationToken);
    }
}
