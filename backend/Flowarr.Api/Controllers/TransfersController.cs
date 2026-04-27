using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController(
    FlowarrDbContext dbContext,
    IOptions<IntegrationOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var transfers = await dbContext.RemoteTransfers
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(transfers);
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var value = options.Value;
        return Ok(new
        {
            value.AutoDownload.Enabled,
            value.AutoDownload.LocalImportPath,
            value.AutoDownload.OverwriteExisting,
            value.AutoDownload.AllowedExtensions,
            sftpSeedbox = new
            {
                configured = !string.IsNullOrWhiteSpace(value.SftpSeedbox.Host) &&
                             !string.IsNullOrWhiteSpace(value.SftpSeedbox.Username) &&
                             (!string.IsNullOrWhiteSpace(value.SftpSeedbox.Password) ||
                              !string.IsNullOrWhiteSpace(value.SftpSeedbox.PrivateKeyPath)),
                value.SftpSeedbox.Host,
                value.SftpSeedbox.Port,
                value.SftpSeedbox.RemoteRootPath,
                usesPrivateKey = !string.IsNullOrWhiteSpace(value.SftpSeedbox.PrivateKeyPath)
            }
        });
    }
}
