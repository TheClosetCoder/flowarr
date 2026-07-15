using Flowarr.Core.Enums;
using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Services;

/// <summary>
/// Auto-generates cleanup candidates on a user-configurable interval (CleanupSettings.AutoGenerateIntervalHours).
/// The interval is a setting, not a fixed timer period, so this polls on a short fixed tick and checks
/// elapsed time against the last successful run - a config change takes effect within one tick, no restart needed.
/// </summary>
public sealed class CleanupScheduleHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CleanupScheduleHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            await TryRunAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var connectionSettingsService = scope.ServiceProvider.GetRequiredService<IConnectionSettingsService>();
            var settings = await connectionSettingsService.GetCleanupSettingsAsync(cancellationToken);
            if (!settings.AutoGenerateEnabled)
            {
                return;
            }

            var dbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
            // SQLite's EF Core provider cannot translate ORDER BY on DateTimeOffset columns, so sort client-side.
            var lastRun = (await dbContext.JobRuns
                .Where(run => run.JobType == "cleanup.generate" && run.Status == JobRunStatus.Succeeded)
                .ToListAsync(cancellationToken))
                .OrderByDescending(run => run.CompletedAt)
                .FirstOrDefault();

            if (lastRun?.CompletedAt is { } completedAt && DateTimeOffset.UtcNow - completedAt < TimeSpan.FromHours(settings.AutoGenerateIntervalHours))
            {
                return;
            }

            var jobRunService = scope.ServiceProvider.GetRequiredService<IJobRunService>();
            var run = await jobRunService.StartAsync("cleanup.generate", cancellationToken);
            await CleanupGenerationRunner.RunTrackedAsync(scopeFactory, run.Id, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled cleanup generation failed");
        }
    }
}
