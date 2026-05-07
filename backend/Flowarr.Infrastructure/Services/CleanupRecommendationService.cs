using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flowarr.Infrastructure.Services;

public sealed class CleanupRecommendationService(
    FlowarrDbContext dbContext,
    IConnectionSettingsService connectionSettingsService,
    IOptions<IntegrationOptions> options) : ICleanupRecommendationService
{
    public async Task<IReadOnlyCollection<CleanupCandidate>> GenerateCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var files = await dbContext.MediaFiles
            .Include(file => file.MediaItem)
            .ToListAsync(cancellationToken);
        var downloads = await dbContext.Downloads.ToListAsync(cancellationToken);
        var existingPendingPaths = await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status == CleanupStatus.PendingReview)
            .Select(candidate => candidate.Path)
            .ToListAsync(cancellationToken);

        var candidates = new List<CleanupCandidate>();

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.RemotePath) || existingPendingPaths.Contains(file.RemotePath))
            {
                continue;
            }

            var relatedDownload = downloads.FirstOrDefault(download =>
                !string.IsNullOrWhiteSpace(download.RemotePath) &&
                file.RemotePath.StartsWith(download.RemotePath, StringComparison.OrdinalIgnoreCase));

            var downloadComplete = relatedDownload?.Status is DownloadStatus.Complete or DownloadStatus.Seeding;
            var knownLibraryItem = file.MediaItem?.ExistsInLibrary == true ||
                                   file.MediaItem?.SourceSystem is "Radarr" or "Sonarr";

            if (file.ExistsOnSeedbox && downloadComplete && file.ImportedLocally && knownLibraryItem)
            {
                candidates.Add(new CleanupCandidate
                {
                    MediaFileId = file.Id,
                    DownloadId = relatedDownload?.Id,
                    Path = file.RemotePath,
                    SizeBytes = file.SizeBytes,
                    Reason = $"Seedbox file appears imported locally. Evidence: download complete={downloadComplete}, imported locally={file.ImportedLocally}, known library item={knownLibraryItem}.",
                    Confidence = relatedDownload is null ? CleanupMatchConfidence.Uncertain : CleanupMatchConfidence.Certain,
                    DryRun = options.Value.Cleanup.DryRun
                });
            }
        }

        dbContext.CleanupCandidates.AddRange(candidates);
        await dbContext.SaveChangesAsync(cancellationToken);
        return candidates;
    }

    public async Task<CleanupCandidate?> ApproveAsync(Guid candidateId, string actor, bool dryRun, string? confirmPath, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.CleanupCandidates.FindAsync([candidateId], cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        if (!dryRun)
        {
            if (!options.Value.Cleanup.AllowDelete)
            {
                throw new InvalidOperationException("Real deletion is disabled. Set Flowarr__Cleanup__AllowDelete=true to enable it.");
            }

            if (!string.Equals(confirmPath, candidate.Path, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Confirmation path must exactly match the cleanup candidate path.");
            }

            if (!await IsInsideAllowedRootAsync(candidate.Path, cancellationToken))
            {
                throw new InvalidOperationException("Cleanup path is outside configured seedbox or local media roots.");
            }
        }

        candidate.Status = dryRun ? CleanupStatus.Approved : CleanupStatus.Deleted;
        candidate.DryRun = dryRun;
        candidate.ReviewedAt = DateTimeOffset.UtcNow;
        candidate.ReviewedBy = actor;

        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = dryRun ? "cleanup.approved.dry_run" : "cleanup.delete.approved",
            TargetType = nameof(CleanupCandidate),
            TargetId = candidate.Id,
            Path = candidate.Path,
            Actor = actor,
            DryRun = dryRun,
            Details = dryRun
                ? "Manual approval recorded. No file was deleted because dry-run mode is enabled."
                : "Manual approval recorded after exact path confirmation and allowed-root validation."
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return candidate;
    }

    private async Task<bool> IsInsideAllowedRootAsync(string path, CancellationToken cancellationToken)
    {
        var seedbox = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        var autoDownload = await connectionSettingsService.GetAutoDownloadAsync(cancellationToken);
        var roots = seedbox.SeedboxRoots
            .Where(root => root.Enabled)
            .Select(root => root.Path)
            .Concat(autoDownload.LocalMediaPaths.Where(root => root.Enabled).Select(root => root.Path))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root.TrimEnd('/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        return roots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                                 path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ||
                                 path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CleanupCandidate?> RejectAsync(Guid candidateId, string actor, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.CleanupCandidates.FindAsync([candidateId], cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        candidate.Status = CleanupStatus.Rejected;
        candidate.ReviewedAt = DateTimeOffset.UtcNow;
        candidate.ReviewedBy = actor;

        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "cleanup.rejected",
            TargetType = nameof(CleanupCandidate),
            TargetId = candidate.Id,
            Path = candidate.Path,
            Actor = actor,
            DryRun = true,
            Details = "Cleanup candidate manually rejected."
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return candidate;
    }
}
