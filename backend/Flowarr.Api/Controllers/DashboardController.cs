using Flowarr.Core.Enums;
using Flowarr.Core.Entities;
using Flowarr.Core.Integrations;
using Flowarr.Core.Models;
using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(
    FlowarrDbContext dbContext,
    IDownloadClient downloadClient,
    IEnumerable<IStorageScanner> storageScanners,
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
                GetImportTitle(transfer),
                transfer.CompletedAt ?? transfer.CreatedAt)))
            .OrderByDescending(import => import.ImportedAt)
            .Take(5)
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
                download.UpdatedAt)).ToList());
    }

    private static string GetImportTitle(RemoteTransfer transfer)
    {
        var localName = Path.GetFileName(transfer.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(localName))
        {
            return localName;
        }

        var remoteName = Path.GetFileName(transfer.RemotePath.TrimEnd('/'));
        return string.IsNullOrWhiteSpace(remoteName) ? transfer.RemotePath : remoteName;
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
            await RefreshStorageAsync(cancellationToken);
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

    private async Task RefreshStorageAsync(CancellationToken cancellationToken)
    {
        foreach (var scanner in storageScanners)
        {
            var snapshots = await scanner.ScanAsync(cancellationToken);
            foreach (var snapshot in snapshots)
            {
                var location = await dbContext.StorageLocations
                    .FirstOrDefaultAsync(item => item.Kind == snapshot.Kind && item.Name == snapshot.Name, cancellationToken);

                if (location is null)
                {
                    location = new StorageLocation
                    {
                        Id = Guid.NewGuid(),
                        Kind = snapshot.Kind,
                        Name = snapshot.Name
                    };
                    dbContext.StorageLocations.Add(location);
                }

                location.PathOrEndpoint = snapshot.PathOrEndpoint;
                location.UsedBytes = snapshot.UsedBytes;
                location.FreeBytes = snapshot.FreeBytes;
                location.LastScannedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
