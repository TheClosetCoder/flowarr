using Flowarr.Core.Entities;

namespace Flowarr.Core.Services;

public interface ICleanupRecommendationService
{
    Task<IReadOnlyCollection<CleanupCandidate>> GenerateCandidatesAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default);
    Task<CleanupCandidate?> ApproveAsync(Guid candidateId, string actor, bool dryRun, string? confirmPath, CancellationToken cancellationToken = default);
    Task<CleanupCandidate?> RejectAsync(Guid candidateId, string actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<BulkActionResult>> BulkApproveAsync(IReadOnlyCollection<Guid> candidateIds, string actor, bool dryRun, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<BulkActionResult>> BulkRejectAsync(IReadOnlyCollection<Guid> candidateIds, string actor, CancellationToken cancellationToken = default);
}

public sealed record BulkActionResult(Guid CandidateId, bool Ok, string? Error);
