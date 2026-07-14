using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Integrations;
using Flowarr.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flowarr.Infrastructure.Services;

public sealed class CleanupRecommendationService(
    FlowarrDbContext dbContext,
    IConnectionSettingsService connectionSettingsService,
    PlexClient plexClient,
    IOptions<IntegrationOptions> options) : ICleanupRecommendationService
{
    public async Task<IReadOnlyCollection<CleanupCandidate>> GenerateCandidatesAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var cleanupSettings = await connectionSettingsService.GetCleanupSettingsAsync(cancellationToken);
        var staleAfter = TimeSpan.FromDays(cleanupSettings.StaleAfterDays);
        var now = DateTimeOffset.UtcNow;

        var libraryItems = await plexClient.GetLibraryItemsAsync(onProgress, cancellationToken);
        if (onProgress is not null)
        {
            await onProgress($"Evaluating {libraryItems.Count} file(s) for staleness and duplicates...");
        }

        // Tracked (not AsNoTracking) so re-matching an already-pending path below updates the
        // existing row in place instead of creating a duplicate - this is also what lets a
        // rescan backfill fields added after a candidate was first created (e.g. series/season).
        var existingPending = await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status == CleanupStatus.PendingReview)
            .ToDictionaryAsync(candidate => candidate.Path, StringComparer.Ordinal, cancellationToken);

        var newCandidates = new List<CleanupCandidate>();
        var touchedPaths = new HashSet<string>(StringComparer.Ordinal);
        var byRatingKey = libraryItems.GroupBy(item => item.RatingKey).ToList();

        foreach (var group in byRatingKey)
        {
            // Plex represents multiple physical copies of the same title as multiple Media
            // entries under one item - keep the largest (best-quality proxy), flag the rest.
            var versions = group.OrderByDescending(item => item.FileSizeBytes).ToList();
            var kept = versions[0];

            foreach (var duplicate in versions.Skip(1))
            {
                TryAddCandidate(newCandidates, existingPending, touchedPaths, duplicate,
                    $"Duplicate version of \"{duplicate.Title}\" - this copy is {FormatGb(duplicate.FileSizeBytes)} vs the {FormatGb(kept.FileSizeBytes)} copy being kept.",
                    CleanupMatchConfidence.Certain, options.Value.Cleanup.DryRun,
                    CleanupReasonType.Duplicate, $"ratingkey:{group.Key}", kept.FilePath);
            }

            var staleReason = GetStaleReason(kept, now, staleAfter);
            if (staleReason is not null)
            {
                TryAddCandidate(newCandidates, existingPending, touchedPaths, kept, staleReason, CleanupMatchConfidence.Certain, options.Value.Cleanup.DryRun,
                    CleanupReasonType.Stale, duplicateGroupKey: null, keptPath: null);
            }
        }

        // Secondary, lower-confidence tier: same normalized title under separate Plex entries
        // that Plex itself did not merge into one item (so no shared Media-group signal above).
        var keptItems = byRatingKey.Select(group => group.OrderByDescending(item => item.FileSizeBytes).First()).ToList();
        var possibleDuplicateTitles = keptItems
            .GroupBy(item => NormalizeTitle(item.Title))
            .Where(group => group.Count() > 1);

        foreach (var titleGroup in possibleDuplicateTitles)
        {
            foreach (var item in titleGroup)
            {
                if (touchedPaths.Contains(item.FilePath))
                {
                    continue;
                }

                TryAddCandidate(newCandidates, existingPending, touchedPaths, item,
                    $"Possible duplicate title: \"{item.Title}\" appears under multiple separate Plex entries. Review manually.",
                    CleanupMatchConfidence.Uncertain, options.Value.Cleanup.DryRun,
                    CleanupReasonType.Duplicate, $"title:{NormalizeTitle(item.Title)}", keptPath: null);
            }
        }

        if (onProgress is not null)
        {
            await onProgress($"Saving {newCandidates.Count} new and {touchedPaths.Count - newCandidates.Count} refreshed candidate(s)...");
        }

        dbContext.CleanupCandidates.AddRange(newCandidates);
        await dbContext.SaveChangesAsync(cancellationToken);
        return newCandidates;
    }

    private static string? GetStaleReason(PlexLibraryItem item, DateTimeOffset now, TimeSpan staleAfter)
    {
        if (item.ViewCount == 0 && item.AddedAt is { } addedAt && now - addedAt > staleAfter)
        {
            return $"Never watched; added {(now - addedAt).Days} days ago.";
        }

        if (item.ViewCount > 0 && item.LastViewedAt is { } lastViewedAt && now - lastViewedAt > staleAfter)
        {
            return $"Not watched in {(now - lastViewedAt).Days} days (last viewed {lastViewedAt:yyyy-MM-dd}).";
        }

        return null;
    }

    private static void TryAddCandidate(
        List<CleanupCandidate> newCandidates,
        Dictionary<string, CleanupCandidate> existingPending,
        HashSet<string> touchedPaths,
        PlexLibraryItem item,
        string reason,
        CleanupMatchConfidence confidence,
        bool dryRun,
        CleanupReasonType reasonType,
        string? duplicateGroupKey,
        string? keptPath)
    {
        // Plex's reported path only resolves to a real file if Plex and Flowarr share the same
        // mount layout - skip rather than present a candidate we can't actually verify or act on.
        if (!File.Exists(item.FilePath))
        {
            return;
        }

        if (!touchedPaths.Add(item.FilePath))
        {
            return;
        }

        if (existingPending.TryGetValue(item.FilePath, out var existing))
        {
            // Already a pending candidate from a prior scan - refresh it in place (tracked
            // entity, so SaveChanges persists this) rather than skipping, so fields added after
            // the candidate was first created (e.g. series/season) get backfilled on rescan.
            existing.SizeBytes = item.FileSizeBytes;
            existing.Reason = reason;
            existing.Confidence = confidence;
            existing.DryRun = dryRun;
            existing.ViewCount = item.ViewCount;
            existing.LastViewedAt = item.LastViewedAt;
            existing.AddedAt = item.AddedAt;
            existing.SeriesTitle = item.SeriesTitle;
            existing.SeasonNumber = item.SeasonNumber;
            existing.EpisodeNumber = item.EpisodeNumber;
            existing.MediaType = item.MediaType;
            existing.ReasonType = reasonType;
            existing.DuplicateGroupKey = duplicateGroupKey;
            existing.KeptPath = keptPath;
            return;
        }

        newCandidates.Add(new CleanupCandidate
        {
            Path = item.FilePath,
            SizeBytes = item.FileSizeBytes,
            Reason = reason,
            Confidence = confidence,
            DryRun = dryRun,
            ViewCount = item.ViewCount,
            LastViewedAt = item.LastViewedAt,
            AddedAt = item.AddedAt,
            SeriesTitle = item.SeriesTitle,
            SeasonNumber = item.SeasonNumber,
            EpisodeNumber = item.EpisodeNumber,
            MediaType = item.MediaType,
            ReasonType = reasonType,
            DuplicateGroupKey = duplicateGroupKey,
            KeptPath = keptPath
        });
    }

    private static string FormatGb(long bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";

    private static string NormalizeTitle(string title)
    {
        var withoutYear = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\(\d{4}\)\s*$", "");
        return withoutYear.Trim().ToLowerInvariant();
    }

    public async Task<CleanupCandidate?> ApproveAsync(Guid candidateId, string actor, bool dryRun, string? confirmPath, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.CleanupCandidates.FindAsync([candidateId], cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        if (!dryRun && !string.Equals(confirmPath, candidate.Path, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Confirmation path must exactly match the cleanup candidate path.");
        }

        await ApplyApprovalAsync(candidate, actor, dryRun, bulk: false, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return candidate;
    }

    public async Task<IReadOnlyCollection<BulkActionResult>> BulkApproveAsync(IReadOnlyCollection<Guid> candidateIds, string actor, bool dryRun, CancellationToken cancellationToken = default)
    {
        // Fail fast with one clear error rather than the same "deletion disabled" failure
        // repeated once per file in the batch.
        if (!dryRun && !options.Value.Cleanup.AllowDelete)
        {
            throw new InvalidOperationException("Real deletion is disabled. Set Flowarr__Cleanup__AllowDelete=true to enable it.");
        }

        var results = new List<BulkActionResult>();
        foreach (var candidateId in candidateIds)
        {
            var candidate = await dbContext.CleanupCandidates.FindAsync([candidateId], cancellationToken);
            if (candidate is null)
            {
                results.Add(new BulkActionResult(candidateId, false, "Candidate not found."));
                continue;
            }

            if (candidate.Status != CleanupStatus.PendingReview)
            {
                results.Add(new BulkActionResult(candidateId, false, $"Already {candidate.Status}."));
                continue;
            }

            try
            {
                await ApplyApprovalAsync(candidate, actor, dryRun, bulk: true, cancellationToken);
                results.Add(new BulkActionResult(candidateId, true, null));
            }
            catch (Exception ex)
            {
                results.Add(new BulkActionResult(candidateId, false, ex.Message));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return results;
    }

    private async Task ApplyApprovalAsync(CleanupCandidate candidate, string actor, bool dryRun, bool bulk, CancellationToken cancellationToken)
    {
        if (!dryRun)
        {
            if (!options.Value.Cleanup.AllowDelete)
            {
                throw new InvalidOperationException("Real deletion is disabled. Set Flowarr__Cleanup__AllowDelete=true to enable it.");
            }

            if (!await IsInsideAllowedRootAsync(candidate.Path, cancellationToken))
            {
                throw new InvalidOperationException("Cleanup path is outside configured seedbox or local media roots.");
            }

            DeleteFile(candidate.Path);
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
                : bulk
                    ? "File deleted as part of a bulk action, after allowed-root validation."
                    : "File deleted after exact path confirmation and allowed-root validation."
        });
    }

    private static void DeleteFile(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
        else
        {
            throw new InvalidOperationException("The file no longer exists at the confirmed path.");
        }
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

        ApplyRejection(candidate, actor, bulk: false);
        await dbContext.SaveChangesAsync(cancellationToken);
        return candidate;
    }

    public async Task<IReadOnlyCollection<BulkActionResult>> BulkRejectAsync(IReadOnlyCollection<Guid> candidateIds, string actor, CancellationToken cancellationToken = default)
    {
        var results = new List<BulkActionResult>();
        foreach (var candidateId in candidateIds)
        {
            var candidate = await dbContext.CleanupCandidates.FindAsync([candidateId], cancellationToken);
            if (candidate is null)
            {
                results.Add(new BulkActionResult(candidateId, false, "Candidate not found."));
                continue;
            }

            if (candidate.Status != CleanupStatus.PendingReview)
            {
                results.Add(new BulkActionResult(candidateId, false, $"Already {candidate.Status}."));
                continue;
            }

            ApplyRejection(candidate, actor, bulk: true);
            results.Add(new BulkActionResult(candidateId, true, null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return results;
    }

    private void ApplyRejection(CleanupCandidate candidate, string actor, bool bulk)
    {
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
            Details = bulk ? "Cleanup candidate rejected as part of a bulk action." : "Cleanup candidate manually rejected."
        });
    }
}
