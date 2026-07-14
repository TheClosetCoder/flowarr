using Flowarr.Core.Enums;

namespace Flowarr.Core.Entities;

public sealed class CleanupCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MediaFileId { get; set; }
    public MediaFile? MediaFile { get; set; }
    public Guid? DownloadId { get; set; }
    public Download? Download { get; set; }
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public CleanupMatchConfidence Confidence { get; set; } = CleanupMatchConfidence.Uncertain;
    public CleanupStatus Status { get; set; } = CleanupStatus.PendingReview;
    public bool DryRun { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public int ViewCount { get; set; }
    public DateTimeOffset? LastViewedAt { get; set; }
    public DateTimeOffset? AddedAt { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? MediaType { get; set; }
    public CleanupReasonType ReasonType { get; set; } = CleanupReasonType.Stale;
    public string? DuplicateGroupKey { get; set; }
    public string? KeptPath { get; set; }
    public bool IsSuggestedKeep { get; set; }
}
