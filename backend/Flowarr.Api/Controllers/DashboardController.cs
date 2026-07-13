using Flowarr.Core.Enums;
using Flowarr.Core.Entities;
using Flowarr.Core.Integrations;
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
    IDownloadClient downloadClient,
    IEnumerable<IStorageScanner> storageScanners,
    IStorageSyncService storageSyncService,
    ILogger<DashboardController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardSummary>> Get(CancellationToken cancellationToken)
    {
        await RefreshDashboardSourcesAsync(cancellationToken);

        var storage = await dbContext.StorageLocations
            .OrderBy(location => location.Kind)
            .ThenBy(location => location.Name)
            .ToListAsync(cancellationToken);
        var downloads = await dbContext.Downloads
            .OrderByDescending(download => download.UpdatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        var activeDownloads = downloads.Count(download => download.Status == DownloadStatus.Downloading);

        var mediaImports = await dbContext.MediaFiles
            .Where(file => file.ImportedLocally)
            .OrderByDescending(file => file.LastSeenAt)
            .Take(5)
            .Select(file => new RecentImport(file.MediaItemId, file.MediaItem!.Title, file.LastSeenAt))
            .ToListAsync(cancellationToken);
        var completedTransfers = await dbContext.RemoteTransfers
            .Where(transfer => transfer.Status == TransferStatus.Completed || transfer.Status == TransferStatus.Skipped)
            .OrderByDescending(transfer => transfer.CompletedAt ?? transfer.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);
        var recentImports = mediaImports
            .Concat(completedTransfers.Select(transfer => new RecentImport(
                transfer.Id,
                GetDisplayTitle(transfer.RemotePath, transfer.LocalPath),
                transfer.CompletedAt ?? transfer.CreatedAt)))
            .OrderByDescending(import => import.ImportedAt)
            .Take(5)
            .ToList();

        var activeTransferRows = await dbContext.RemoteTransfers
            .Where(transfer => transfer.Status == TransferStatus.Queued || transfer.Status == TransferStatus.Running)
            .OrderBy(transfer => transfer.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        var activeTransfers = activeTransferRows
            .Select(transfer => new ActiveTransferSummary(
                transfer.Id,
                GetDisplayTitle(transfer.RemotePath, transfer.LocalPath),
                transfer.Status.ToString(),
                transfer.BytesTransferred,
                transfer.TotalBytes,
                transfer.CreatedAt))
            .ToList();

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
            activeTransfers);
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

    private async Task RefreshDashboardSourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshDownloadsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard torrent client refresh failed; returning last known download data.");
        }

        try
        {
            await storageSyncService.SyncAsync(storageScanners, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard storage refresh failed; returning last known storage data.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
