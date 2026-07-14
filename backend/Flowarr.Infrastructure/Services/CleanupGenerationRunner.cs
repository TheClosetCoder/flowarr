using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Services;

/// <summary>
/// Runs a cleanup candidate scan against an already-started JobRun, reporting progress and
/// completing/failing it. Shared by the manual "Generate recommendations" endpoint and the
/// scheduled auto-generate hosted service so both go through identical tracking logic.
/// </summary>
public static class CleanupGenerationRunner
{
    public static async Task RunTrackedAsync(
        IServiceScopeFactory scopeFactory,
        Guid jobRunId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
        var scopedJobRunService = scope.ServiceProvider.GetRequiredService<IJobRunService>();
        var scopedCleanupService = scope.ServiceProvider.GetRequiredService<ICleanupRecommendationService>();

        var trackedRun = await scopedDbContext.JobRuns.FindAsync([jobRunId], cancellationToken);
        if (trackedRun is null)
        {
            return;
        }

        try
        {
            await scopedCleanupService.GenerateCandidatesAsync(
                onProgress: message => scopedJobRunService.ReportProgressAsync(trackedRun, message, cancellationToken),
                cancellationToken: cancellationToken);
            await scopedJobRunService.CompleteAsync(trackedRun, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cleanup candidate generation failed");
            await scopedJobRunService.FailAsync(trackedRun, ex, cancellationToken);
        }
    }
}
