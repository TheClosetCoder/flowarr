using Flowarr.Core.Entities;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
            var jobRuns = scope.ServiceProvider.GetRequiredService<IJobRunService>();
            var run = await jobRuns.StartAsync("sync.all", cancellationToken);
            await SyncDownloadsAsync(scope.ServiceProvider, dbContext, cancellationToken);
            await SyncMediaAsync(scope.ServiceProvider, dbContext, cancellationToken);
            await SyncStorageAsync(scope.ServiceProvider, dbContext, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await jobRuns.CompleteAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Flowarr sync failed");
        }
    }

    private static async Task SyncDownloadsAsync(IServiceProvider services, FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        var client = services.GetRequiredService<IDownloadClient>();
        var snapshots = await client.GetDownloadsAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var download = await dbContext.Downloads.FirstOrDefaultAsync(item => item.Hash == snapshot.Hash, cancellationToken);
            if (download is null)
            {
                download = new Download { Id = Guid.NewGuid(), Hash = snapshot.Hash };
                dbContext.Downloads.Add(download);
            }

            download.Client = snapshot.Client;
            download.Name = snapshot.Name;
            download.Status = snapshot.Status;
            download.Progress = snapshot.Progress;
            download.SizeBytes = snapshot.SizeBytes;
            download.RemotePath = snapshot.RemotePath;
            download.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static async Task SyncMediaAsync(IServiceProvider services, FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        var client = services.GetRequiredService<IMediaCatalogClient>();
        var snapshots = await client.GetMediaItemsAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var mediaItem = await dbContext.MediaItems
                .Include(item => item.Files)
                .FirstOrDefaultAsync(item => item.SourceSystem == snapshot.SourceSystem && item.ExternalId == snapshot.ExternalId, cancellationToken);

            if (mediaItem is null)
            {
                mediaItem = new MediaItem { Id = Guid.NewGuid() };
                dbContext.MediaItems.Add(mediaItem);
            }

            mediaItem.Title = snapshot.Title;
            mediaItem.ExternalId = snapshot.ExternalId;
            mediaItem.SourceSystem = snapshot.SourceSystem;
            mediaItem.ExistsInLibrary = snapshot.ExistsInLibrary;

            foreach (var fileSnapshot in snapshot.Files)
            {
                var file = mediaItem.Files.FirstOrDefault(item => item.Path == fileSnapshot.Path);
                if (file is null)
                {
                    file = new MediaFile { Id = Guid.NewGuid(), Path = fileSnapshot.Path };
                    mediaItem.Files.Add(file);
                }

                file.RemotePath = fileSnapshot.RemotePath;
                file.SizeBytes = fileSnapshot.SizeBytes;
                file.ExistsLocally = fileSnapshot.ExistsLocally;
                file.ExistsOnSeedbox = fileSnapshot.ExistsOnSeedbox;
                file.ImportedLocally = fileSnapshot.ImportedLocally;
                file.LastSeenAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static async Task SyncStorageAsync(IServiceProvider services, FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        var scanners = services.GetServices<IStorageScanner>();
        foreach (var scanner in scanners)
        {
            var snapshots = await scanner.ScanAsync(cancellationToken: cancellationToken);
            foreach (var snapshot in snapshots)
            {
                var location = await dbContext.StorageLocations
                    .FirstOrDefaultAsync(item => item.Kind == snapshot.Kind && item.Name == snapshot.Name, cancellationToken);

                if (location is null)
                {
                    location = new StorageLocation { Id = Guid.NewGuid(), Kind = snapshot.Kind, Name = snapshot.Name };
                    dbContext.StorageLocations.Add(location);
                }

                location.PathOrEndpoint = snapshot.PathOrEndpoint;
                location.UsedBytes = snapshot.UsedBytes;
                location.FreeBytes = snapshot.FreeBytes;
                location.LastScannedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
