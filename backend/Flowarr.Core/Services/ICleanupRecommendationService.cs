using Flowarr.Core.Entities;

namespace Flowarr.Core.Services;

public interface ICleanupRecommendationService
{
    Task<IReadOnlyCollection<CleanupCandidate>> GenerateCandidatesAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default);
    Task<CleanupCandidate?> ApproveAsync(Guid candidateId, string actor, bool dryRun, string? confirmPath, CancellationToken cancellationToken = default);
    Task<CleanupCandidate?> RejectAsync(Guid candidateId, string actor, CancellationToken cancellationToken = default);
}
