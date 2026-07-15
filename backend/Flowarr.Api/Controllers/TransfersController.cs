using Flowarr.Core.Enums;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController(
    FlowarrDbContext dbContext,
    IConnectionSettingsService connectionSettingsService,
    IActiveTransferCancellationRegistry cancellationRegistry) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // SQLite's EF Core provider cannot translate ORDER BY on DateTimeOffset columns, so sort/Take client-side.
        var transfers = (await dbContext.RemoteTransfers.ToListAsync(cancellationToken))
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Take(100)
            .ToList();

        return Ok(transfers);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var transfer = await dbContext.RemoteTransfers.FindAsync([id], cancellationToken);
        if (transfer is null)
        {
            return NotFound();
        }

        switch (transfer.Status)
        {
            case TransferStatus.Queued:
                dbContext.RemoteTransfers.Remove(transfer);
                await dbContext.SaveChangesAsync(cancellationToken);
                return NoContent();

            case TransferStatus.Running:
                var cancelled = cancellationRegistry.Cancel(id);
                if (!cancelled)
                {
                    return Conflict(new { message = "This transfer is no longer running." });
                }

                return Accepted();

            default:
                return BadRequest(new { message = $"Cannot cancel a transfer with status {transfer.Status}." });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var transfer = await dbContext.RemoteTransfers.FindAsync([id], cancellationToken);
        if (transfer is null)
        {
            return NotFound();
        }

        if (transfer.Status == TransferStatus.Running)
        {
            return BadRequest(new { message = "Cancel the transfer before removing it." });
        }

        dbContext.RemoteTransfers.Remove(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var value = await connectionSettingsService.GetSnapshotAsync(cancellationToken);
        return Ok(new
        {
            value.AutoDownload.Enabled,
            value.AutoDownload.LocalImportPath,
            value.AutoDownload.OverwriteExisting,
            value.AutoDownload.AllowedExtensions,
            value.AutoDownload.LocalMediaPaths,
            value.AutoDownload.TransferRules,
            sftpSeedbox = new
            {
                configured = value.SftpSeedbox.Enabled &&
                             !string.IsNullOrWhiteSpace(value.SftpSeedbox.Host) &&
                             !string.IsNullOrWhiteSpace(value.SftpSeedbox.Username) &&
                             (!string.IsNullOrWhiteSpace(value.SftpSeedbox.Password) ||
                              !string.IsNullOrWhiteSpace(value.SftpSeedbox.PrivateKeyPath)),
                value.SftpSeedbox.Host,
                value.SftpSeedbox.Port,
                usesPrivateKey = !string.IsNullOrWhiteSpace(value.SftpSeedbox.PrivateKeyPath)
            }
        });
    }
}
