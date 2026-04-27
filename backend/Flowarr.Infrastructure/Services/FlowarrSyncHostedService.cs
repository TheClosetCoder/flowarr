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
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureCompatibilityTablesAsync(dbContext, cancellationToken);

            await LegacyMockDataCleaner.RemoveAsync(dbContext, cancellationToken);
            await SyncDownloadsAsync(scope.ServiceProvider, dbContext, cancellationToken);
            await SyncMediaAsync(scope.ServiceProvider, dbContext, cancellationToken);
            await SyncStorageAsync(scope.ServiceProvider, dbContext, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Flowarr sync failed");
        }
    }

    private static async Task EnsureCompatibilityTablesAsync(FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "IntegrationConnections" (
                "Id" uuid NOT NULL,
                "Key" text NOT NULL,
                "DisplayName" text NOT NULL,
                "Enabled" boolean NOT NULL,
                "SettingsJson" text NOT NULL,
                "SecretsJson" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_IntegrationConnections" PRIMARY KEY ("Id")
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RemoteTransfers" (
                "Id" uuid NOT NULL,
                "DownloadId" uuid NULL,
                "Provider" text NOT NULL,
                "RemotePath" text NOT NULL,
                "LocalPath" text NOT NULL,
                "BytesTransferred" bigint NOT NULL,
                "Status" integer NOT NULL,
                "Message" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "StartedAt" timestamp with time zone NULL,
                "CompletedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_RemoteTransfers" PRIMARY KEY ("Id")
            );
            """,
            cancellationToken);
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
            var snapshots = await scanner.ScanAsync(cancellationToken);
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
