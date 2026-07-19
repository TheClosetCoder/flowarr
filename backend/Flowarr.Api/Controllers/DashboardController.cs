using Flowarr.Core.Enums;
using Flowarr.Core.Entities;
using Flowarr.Core.Models;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(
    FlowarrDbContext dbContext,
    IConnectionSettingsService connectionSettingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardSummary>> Get(CancellationToken cancellationToken)
    {
        var storage = await dbContext.StorageLocations
            .OrderBy(location => location.Kind)
            .ThenBy(location => location.Name)
            .ToListAsync(cancellationToken);
        // SQLite's EF Core provider cannot translate ORDER BY on DateTimeOffset columns, so these
        // sort/Take client-side after a (still server-filtered) fetch instead of chaining OrderBy
        // before ToListAsync. Table sizes here are homelab-scale, so this is cheap.
        var downloads = (await dbContext.Downloads.ToListAsync(cancellationToken))
            .OrderByDescending(download => download.UpdatedAt)
            .Take(10)
            .ToList();
        var activeDownloads = downloads.Count(download => download.Status == DownloadStatus.Downloading);

        var mediaImportFiles = await dbContext.MediaFiles
            .Include(file => file.MediaItem)
            .Where(file => file.ImportedLocally)
            .ToListAsync(cancellationToken);
        var mediaImports = mediaImportFiles
            .OrderByDescending(file => file.LastSeenAt)
            .Take(10)
            .Select(file => new RecentImport(file.MediaItemId, file.MediaItem!.Title, file.LastSeenAt))
            .ToList();
        var completedTransfers = (await dbContext.RemoteTransfers
            .Where(transfer => transfer.Status == TransferStatus.Completed || transfer.Status == TransferStatus.Skipped)
            .ToListAsync(cancellationToken))
            .OrderByDescending(transfer => transfer.CompletedAt ?? transfer.CreatedAt)
            .Take(10)
            .ToList();
        var recentImports = mediaImports
            .Concat(completedTransfers.Select(transfer => new RecentImport(
                transfer.Id,
                GetDisplayTitle(transfer.RemotePath, transfer.LocalPath),
                transfer.CompletedAt ?? transfer.CreatedAt)))
            .OrderByDescending(import => import.ImportedAt)
            .Take(10)
            .ToList();

        var activeTransferRows = (await dbContext.RemoteTransfers
            .Where(transfer => transfer.Status == TransferStatus.Queued || transfer.Status == TransferStatus.Running)
            .ToListAsync(cancellationToken))
            .OrderBy(transfer => transfer.CreatedAt)
            .Take(10)
            .ToList();
        var activeTransfers = activeTransferRows
            .Select(transfer => new ActiveTransferSummary(
                transfer.Id,
                GetDisplayTitle(transfer.RemotePath, transfer.LocalPath),
                transfer.Status.ToString(),
                transfer.BytesTransferred,
                transfer.TotalBytes,
                transfer.CreatedAt))
            .ToList();

        var cleanupCandidates = (await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status == CleanupStatus.PendingReview)
            .ToListAsync(cancellationToken))
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(5)
            .Select(candidate => new CleanupCandidateSummary(
                candidate.Id,
                candidate.Path,
                candidate.SizeBytes,
                candidate.Reason,
                candidate.Confidence.ToString(),
                candidate.DryRun,
                candidate.SeriesTitle,
                candidate.SeasonNumber,
                candidate.ReasonType.ToString(),
                candidate.DuplicateGroupKey))
            .ToList();

        var cleanupStats = await BuildCleanupStatsAsync(cancellationToken);

        var local = storage.Where(item => item.Kind == StorageKind.Local);
        var seedbox = storage.Where(item => item.Kind == StorageKind.Seedbox);

        return new DashboardSummary(
            new StorageSummary(local.Sum(item => item.UsedBytes), local.Sum(item => item.FreeBytes)),
            new StorageSummary(seedbox.Sum(item => item.UsedBytes), seedbox.Sum(item => item.FreeBytes)),
            activeDownloads,
            recentImports,
            cleanupCandidates,
            storage.Select(location => new StorageLocationSummary(
                location.Name,
                location.Kind.ToString(),
                location.PathOrEndpoint,
                location.UsedBytes,
                location.FreeBytes,
                location.LastScannedAt)).ToList(),
            downloads.Select(download => new DownloadSummary(
                download.Id,
                download.Client,
                download.Name,
                download.Status.ToString(),
                download.Progress,
                download.SizeBytes,
                download.RemotePath,
                download.UpdatedAt)).ToList(),
            activeTransfers,
            cleanupStats);
    }

    private async Task<CleanupStats> BuildCleanupStatsAsync(CancellationToken cancellationToken)
    {
        var pending = await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status == CleanupStatus.PendingReview && !candidate.IsSuggestedKeep)
            .GroupBy(candidate => candidate.ReasonType)
            .Select(group => new { ReasonType = group.Key, Count = group.Count(), Bytes = group.Sum(candidate => candidate.SizeBytes) })
            .ToListAsync(cancellationToken);

        var duplicates = pending.FirstOrDefault(group => group.ReasonType == CleanupReasonType.Duplicate);
        var stale = pending.FirstOrDefault(group => group.ReasonType == CleanupReasonType.Stale);

        var lifetimeFreedBytes = await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status == CleanupStatus.Deleted)
            .SumAsync(candidate => candidate.SizeBytes, cancellationToken);

        var lastRun = (await dbContext.JobRuns
            .Where(run => run.JobType == "cleanup.generate" && run.Status == JobRunStatus.Succeeded)
            .ToListAsync(cancellationToken))
            .OrderByDescending(run => run.CompletedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        var cleanupSettings = await connectionSettingsService.GetCleanupSettingsAsync(cancellationToken);
        var nextScanAt = cleanupSettings.AutoGenerateEnabled && lastRun?.CompletedAt is { } lastCompletedAt
            ? lastCompletedAt.AddHours(cleanupSettings.AutoGenerateIntervalHours)
            : (DateTimeOffset?)null;

        return new CleanupStats(
            duplicates?.Count ?? 0,
            duplicates?.Bytes ?? 0,
            stale?.Count ?? 0,
            stale?.Bytes ?? 0,
            lifetimeFreedBytes,
            lastRun?.CompletedAt,
            nextScanAt);
    }

    private static string GetDisplayTitle(string remotePath, string localPath)
    {
        // RemotePath is the actual per-item seedbox folder (e.g. ".../Movies/Project Hail Mary (2026)"),
        // while LocalPath is often just the rule's shared destination folder (e.g. "/media/Movies") and
        // gives the same generic name for every item downloaded through that rule.
        var remoteName = Path.GetFileName(remotePath.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(remoteName))
        {
            return remoteName;
        }

        var localName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(localName) ? remotePath : localName;
    }
}
