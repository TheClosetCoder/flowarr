namespace Flowarr.Core.Models;

public sealed record DashboardSummary(
    StorageSummary LocalStorage,
    StorageSummary SeedboxStorage,
    int ActiveDownloads,
    IReadOnlyCollection<RecentImport> RecentImports,
    IReadOnlyCollection<CleanupCandidateSummary> CleanupCandidates);

public sealed record StorageSummary(long UsedBytes, long FreeBytes);

public sealed record RecentImport(Guid MediaItemId, string Title, DateTimeOffset ImportedAt);

public sealed record CleanupCandidateSummary(Guid Id, string Path, long SizeBytes, string Reason, string Confidence, bool DryRun);
