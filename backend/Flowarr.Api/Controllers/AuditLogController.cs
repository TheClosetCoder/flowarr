using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/audit-log")]
public sealed class AuditLogController(FlowarrDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // SQLite's EF Core provider cannot translate ORDER BY on DateTimeOffset columns, so sort/Take client-side.
        var entries = (await dbContext.AuditLogs.ToListAsync(cancellationToken))
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(100)
            .ToList();

        return Ok(entries);
    }
}
