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

        // Tracked (not AsNoTracking) so re-matching an already-known path below updates the
        // existing row in place instead of creating a duplicate. Includes Approved/Rejected/
        // Failed, not just PendingReview: those statuses mean a decision was recorded but the
        // file is still on disk (dry-run approve, reject, or a failed real delete), so a rescan
        // must not re-flag them as a fresh pending candidate and forget the decision. Deleted
        // candidates are excluded - the file is gone, so a rematch there can't happen anyway.
        var existingRows = await dbContext.CleanupCandidates
            .Where(candidate => candidate.Status != CleanupStatus.Deleted)
            .ToListAsync(cancellationToken);

        // Earlier scans (before rescans matched decided candidates too) could leave more than
        // one row for the same path - e.g. a Rejected row plus a freshly re-added PendingReview
        // row. Collapse those to one row per path so lookups below are unambiguous: keep the
        // decided row if there is one, otherwise the newest, and drop the rest.
        var existingByPath = new Dictionary<string, CleanupCandidate>(StringComparer.Ordinal);
        foreach (var group in existingRows.GroupBy(candidate => candidate.Path, StringComparer.Ordinal))
        {
            var primary = group.Count() == 1
                ? group.Single()
                : group.FirstOrDefault(candidate => candidate.Status != CleanupStatus.PendingReview)
                    ?? group.OrderByDescending(candidate => candidate.CreatedAt).First();

            existingByPath[group.Key] = primary;

            foreach (var duplicate in group.Where(candidate => candidate.Id != primary.Id))
            {
                dbContext.CleanupCandidates.Remove(duplicate);
            }
        }

        var newCandidates = new List<CleanupCandidate>();
        var touchedPaths = new HashSet<string>(StringComparer.Ordinal);
        var byRatingKey = libraryItems.GroupBy(item => item.RatingKey).ToList();

        foreach (var group in byRatingKey)
        {
            // Plex represents multiple physical copies of the same title as multiple Media
            // entries under one item - keep the largest (best-quality proxy), flag the rest.
            var versions = group.OrderByDescending(item => item.FileSizeBytes).ToList();
            var kept = versions[0];
            var duplicateVersions = versions.Skip(1).ToList();

            // Whether at least one sibling copy is still awaiting a decision. A sibling that was
            // rejected (kept on disk, decision recorded) or deleted no longer needs action, so it
            // shouldn't keep the group's "suggested keep" marker alive on its own. Deleted rows
            // are excluded from existingByPath, so a deleted sibling looks "not found" here - the
            // File.Exists check is what actually distinguishes that from a genuinely new,
            // never-seen-before duplicate (which is also "not found" but very much still pending).
            var anyDuplicateStillPending = duplicateVersions.Any(duplicate =>
                File.Exists(duplicate.FilePath) &&
                (!existingByPath.TryGetValue(duplicate.FilePath, out var existingDuplicate)
                    || existingDuplicate.Status == CleanupStatus.PendingReview));

            foreach (var duplicate in duplicateVersions)
            {
                TryAddCandidate(newCandidates, existingByPath, touchedPaths, duplicate,
                    $"Duplicate version of \"{duplicate.Title}\" - this copy is {FormatGb(duplicate.FileSizeBytes)} vs the {FormatGb(kept.FileSizeBytes)} copy being kept.",
                    CleanupMatchConfidence.Certain, options.Value.Cleanup.DryRun,
                    CleanupReasonType.Duplicate, $"ratingkey:{group.Key}", kept.FilePath, isSuggestedKeep: false);
            }

            CleanupCandidate? leftoverKeepMarker = null;
            if (duplicateVersions.Count > 0 && anyDuplicateStillPending)
            {
                // Also surface the largest copy as a candidate in its own group - flagged only as
                // the suggested keeper, not for deletion - so the user can pick a different copy
                // to keep instead of being locked into the size-based default.
                var otherCount = duplicateVersions.Count;
                TryAddCandidate(newCandidates, existingByPath, touchedPaths, kept,
                    $"Largest copy of \"{kept.Title}\" ({FormatGb(kept.FileSizeBytes)}) - suggested to keep over {otherCount} other cop{(otherCount == 1 ? "y" : "ies")}.",
                    CleanupMatchConfidence.Certain, options.Value.Cleanup.DryRun,
                    CleanupReasonType.Duplicate, $"ratingkey:{group.Key}", kept.FilePath, isSuggestedKeep: true);
            }
            else if (existingByPath.TryGetValue(kept.FilePath, out var existingKept)
                && existingKept.Status == CleanupStatus.PendingReview
                && existingKept.IsSuggestedKeep)
            {
                // Every sibling duplicate has been resolved (deleted or rejected) - or there
                // never were any left after Plex's report changed - so this marker no longer
                // means anything. Remove it unless the staleness check below reclaims the row.
                leftoverKeepMarker = existingKept;
            }

            var staleReason = GetStaleReason(kept, now, staleAfter);
            if (staleReason is not null)
            {
                TryAddCandidate(newCandidates, existingByPath, touchedPaths, kept, staleReason, CleanupMatchConfidence.Certain, options.Value.Cleanup.DryRun,
                    CleanupReasonType.Stale, duplicateGroupKey: null, keptPath: null);
            }
            else if (leftoverKeepMarker is not null)
            {
                dbContext.CleanupCandidates.Remove(leftoverKeepMarker);
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

                TryAddCandidate(newCandidates, existingByPath, touchedPaths, item,
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
        Dictionary<string, CleanupCandidate> existingByPath,
        HashSet<string> touchedPaths,
        PlexLibraryItem item,
        string reason,
        CleanupMatchConfidence confidence,
        bool dryRun,
        CleanupReasonType reasonType,
        string? duplicateGroupKey,
        string? keptPath,
        bool isSuggestedKeep = false)
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

        if (existingByPath.TryGetValue(item.FilePath, out var existing))
        {
            // Descriptive/matching fields always get refreshed - including for a decided
            // (Approved/Rejected/Failed) row - so metadata added after the candidate was first
            // created (e.g. series/season) gets backfilled regardless of decision state.
            existing.SizeBytes = item.FileSizeBytes;
            existing.Confidence = confidence;
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
            existing.IsSuggestedKeep = isSuggestedKeep;

            if (existing.Status == CleanupStatus.PendingReview)
            {
                // Reason/DryRun describe the decision itself once one has been recorded (approve
                // dry-run / reject / a failed real delete) - only touch them while still pending,
                // so a rescan can't quietly rewrite what the user already decided.
                existing.Reason = reason;
                existing.DryRun = dryRun;
            }

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
            KeptPath = keptPath,
            IsSuggestedKeep = isSuggestedKeep
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
