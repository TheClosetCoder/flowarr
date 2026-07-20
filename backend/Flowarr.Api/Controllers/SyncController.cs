using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController(
    IJobRunService jobRunService,
    IServiceScopeFactory scopeFactory,
    ILogger<SyncController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var run = await jobRunService.StartAsync("sync.all", cancellationToken);

        _ = Task.Run(() => FlowarrSyncRunner.RunTrackedAsync(scopeFactory, run.Id, logger, CancellationToken.None));

        return Accepted(new { jobId = run.Id });
    }
}
