using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/downloads")]
public sealed class DownloadsController(FlowarrDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var downloads = await dbContext.Downloads
            .OrderByDescending(download => download.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(downloads);
    }
}
