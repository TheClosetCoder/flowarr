using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Services;

/// <summary>
/// Runs a storage scan against an already-started JobRun, reporting progress and
/// completing/failing it. Mirrors CleanupGenerationRunner for the manual "Scan now" endpoint.
/// </summary>
public static class StorageScanRunner
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
        var scopedStorageSyncService = scope.ServiceProvider.GetRequiredService<IStorageSyncService>();
        var scopedScanners = scope.ServiceProvider.GetServices<IStorageScanner>();

        var trackedRun = await scopedDbContext.JobRuns.FindAsync([jobRunId], cancellationToken);
        if (trackedRun is null)
        {
            return;
        }

        try
        {
            await scopedStorageSyncService.SyncAsync(
                scopedScanners,
                onProgress: message => scopedJobRunService.ReportProgressAsync(trackedRun, message, cancellationToken),
                cancellationToken: cancellationToken);
            await scopedJobRunService.CompleteAsync(trackedRun, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storage scan failed");
            await scopedJobRunService.FailAsync(trackedRun, ex, cancellationToken);
        }
    }
}
