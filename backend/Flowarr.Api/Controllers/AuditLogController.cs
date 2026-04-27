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
        var entries = await dbContext.AuditLogs
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(entries);
    }
}
