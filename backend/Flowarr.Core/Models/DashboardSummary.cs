namespace Flowarr.Core.Models;

public sealed record DashboardSummary(
    StorageSummary LocalStorage,
    StorageSummary SeedboxStorage,
    int ActiveDownloads,
    IReadOnlyCollection<RecentImport> RecentImports,
    IReadOnlyCollection<CleanupCandidateSummary> CleanupCandidates,
    IReadOnlyCollection<StorageLocationSummary> StorageLocations,
    IReadOnlyCollection<DownloadSummary> Downloads);

public sealed record StorageSummary(long UsedBytes, long FreeBytes);

public sealed record RecentImport(Guid MediaItemId, string Title, DateTimeOffset ImportedAt);

public sealed record CleanupCandidateSummary(Guid Id, string Path, long SizeBytes, string Reason, string Confidence, bool DryRun);

public sealed record StorageLocationSummary(
    string Name,
    string Kind,
    string PathOrEndpoint,
    long UsedBytes,
    long FreeBytes,
    DateTimeOffset LastScannedAt);

public sealed record DownloadSummary(
    Guid Id,
    string Client,
    string Name,
    string Status,
    decimal Progress,
    long SizeBytes,
    string? RemotePath,
    DateTimeOffset UpdatedAt);
