using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Services;

public sealed class FlowarrSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<FlowarrSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncOnceAsync(stoppingToken);
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        // This must never let an exception escape - BackgroundService.ExecuteAsync propagating one
        // brings down the entire host by default (HostOptions.BackgroundServiceExceptionBehavior).
        // FlowarrSyncRunner already logs/records failures internally, but if even that recording step
        // throws (e.g. a transient DB error), it must not take the whole app down with it.
        try
        {
            using var scope = scopeFactory.CreateScope();
            var jobRuns = scope.ServiceProvider.GetRequiredService<IJobRunService>();
            var run = await jobRuns.StartAsync("sync.all", cancellationToken);
            await FlowarrSyncRunner.RunTrackedAsync(scopeFactory, run.Id, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Flowarr sync cycle failed unexpectedly");
        }
    }
}
