using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/storage")]
public sealed class StorageController(
    FlowarrDbContext dbContext,
    IJobRunService jobRunService,
    IServiceScopeFactory scopeFactory,
    ILogger<StorageController> logger) : ControllerBase
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
        var run = await jobRunService.StartAsync("storage.scan", cancellationToken);

        _ = Task.Run(() => StorageScanRunner.RunTrackedAsync(scopeFactory, run.Id, logger, CancellationToken.None));

        return Accepted(new { jobId = run.Id });
    }
}
