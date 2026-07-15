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
        // SQLite's EF Core provider cannot translate ORDER BY on DateTimeOffset columns, so sort/Take client-side.
        var runs = (await dbContext.JobRuns.ToListAsync(cancellationToken))
            .OrderByDescending(run => run.StartedAt)
            .Take(100)
            .ToList();

        return Ok(runs);
    }
}
