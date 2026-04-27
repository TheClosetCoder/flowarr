using Flowarr.Core.Entities;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/downloads")]
public sealed class DownloadsController(
    FlowarrDbContext dbContext,
    IDownloadClient downloadClient,
    ILogger<DownloadsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshDownloadsAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Download refresh failed; returning last known download data.");
        }

        var downloads = await dbContext.Downloads
            .OrderByDescending(download => download.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(downloads);
    }

    private async Task RefreshDownloadsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await downloadClient.GetDownloadsAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var download = await FindDownloadAsync(snapshot, cancellationToken);
            if (download is null)
            {
                download = new Download { Id = Guid.NewGuid(), Hash = snapshot.Hash };
                dbContext.Downloads.Add(download);
            }

            download.Client = snapshot.Client;
            download.Name = snapshot.Name;
            download.Hash = snapshot.Hash;
            download.Status = snapshot.Status;
            download.Progress = snapshot.Progress;
            download.SizeBytes = snapshot.SizeBytes;
            download.RemotePath = snapshot.RemotePath;
            download.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private Task<Download?> FindDownloadAsync(DownloadSnapshot snapshot, CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(snapshot.Hash)
            ? dbContext.Downloads.FirstOrDefaultAsync(download =>
                download.Client == snapshot.Client &&
                download.Name == snapshot.Name &&
                download.RemotePath == snapshot.RemotePath,
                cancellationToken)
            : dbContext.Downloads.FirstOrDefaultAsync(download => download.Hash == snapshot.Hash, cancellationToken);
    }
}
