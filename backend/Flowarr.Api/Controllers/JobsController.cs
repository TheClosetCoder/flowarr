using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(FlowarrDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var runs = await dbContext.JobRuns
            .OrderByDescending(run => run.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(runs);
    }
}
