using Flowarr.Core.Enums;
using Flowarr.Core.Models;
using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(FlowarrDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardSummary>> Get(CancellationToken cancellationToken)
    {
        var storage = await dbContext.StorageLocations.ToListAsync(cancellationToken);
        var activeDownloads = await dbContext.Downloads.CountAsync(download =>
            download.Status == DownloadStatus.Downloading, cancellationToken);

        var recentImports = await dbContext.MediaFiles
            .Where(file => file.ImportedLocally)
            .OrderByDescending(file => file.LastSeenAt)
            .Take(5)
            .Select(file => new RecentImport(file.MediaItemId, file.MediaItem!.Title, file.LastSeenAt))
            .ToListAsync(cancellationToken);

        var cleanupCandidates = await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status == CleanupStatus.PendingReview)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(5)
            .Select(candidate => new CleanupCandidateSummary(
                candidate.Id,
                candidate.Path,
                candidate.SizeBytes,
                candidate.Reason,
                candidate.Confidence.ToString(),
                candidate.DryRun))
            .ToListAsync(cancellationToken);

        var local = storage.Where(item => item.Kind == StorageKind.Local);
        var seedbox = storage.Where(item => item.Kind == StorageKind.Seedbox);

        return new DashboardSummary(
            new StorageSummary(local.Sum(item => item.UsedBytes), local.Sum(item => item.FreeBytes)),
            new StorageSummary(seedbox.Sum(item => item.UsedBytes), seedbox.Sum(item => item.FreeBytes)),
            activeDownloads,
            recentImports,
            cleanupCandidates);
    }
}
