using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowarr.Infrastructure.Services;

public sealed class AutoDownloadHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<IntegrationOptions> options,
    ILogger<AutoDownloadHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoDownload.Enabled)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
            var transferClient = scope.ServiceProvider.GetRequiredService<IRemoteFileTransferClient>();

            if (!transferClient.IsConfigured)
            {
                logger.LogInformation("Auto-download is enabled, but SFTP seedbox settings are incomplete.");
                return;
            }

            var downloads = await dbContext.Downloads
                .Where(download =>
                    !string.IsNullOrWhiteSpace(download.RemotePath) &&
                    (download.Status == DownloadStatus.Complete || download.Status == DownloadStatus.Seeding))
                .OrderBy(download => download.UpdatedAt)
                .Take(10)
                .ToListAsync(cancellationToken);

            foreach (var download in downloads)
            {
                var alreadyHandled = await dbContext.RemoteTransfers.AnyAsync(transfer =>
                    transfer.RemotePath == download.RemotePath &&
                    (transfer.Status == TransferStatus.Completed || transfer.Status == TransferStatus.Running || transfer.Status == TransferStatus.Skipped),
                    cancellationToken);

                if (alreadyHandled)
                {
                    continue;
                }

                await DownloadAsync(dbContext, transferClient, download, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-download sync failed");
        }
    }

    private async Task DownloadAsync(
        FlowarrDbContext dbContext,
        IRemoteFileTransferClient transferClient,
        Download download,
        CancellationToken cancellationToken)
    {
        var transfer = new RemoteTransfer
        {
            DownloadId = download.Id,
            Provider = "SFTP",
            RemotePath = download.RemotePath!,
            LocalPath = options.Value.AutoDownload.LocalImportPath,
            Status = TransferStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        dbContext.RemoteTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await transferClient.DownloadAsync(download.RemotePath!, options.Value.AutoDownload.LocalImportPath, cancellationToken);
            transfer.BytesTransferred = result.BytesTransferred;
            transfer.LocalPath = result.LocalPath;
            transfer.Status = result.Skipped ? TransferStatus.Skipped : TransferStatus.Completed;
            transfer.Message = result.Message;
            transfer.CompletedAt = DateTimeOffset.UtcNow;

            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = result.Skipped ? "transfer.skipped" : "transfer.completed",
                TargetType = nameof(RemoteTransfer),
                TargetId = transfer.Id,
                Path = result.RemotePath,
                Actor = "auto-download",
                DryRun = false,
                Details = $"{result.Message} Bytes transferred: {result.BytesTransferred}."
            });
        }
        catch (Exception ex)
        {
            transfer.Status = TransferStatus.Failed;
            transfer.Message = ex.Message;
            transfer.CompletedAt = DateTimeOffset.UtcNow;

            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "transfer.failed",
                TargetType = nameof(RemoteTransfer),
                TargetId = transfer.Id,
                Path = transfer.RemotePath,
                Actor = "auto-download",
                DryRun = false,
                Details = ex.Message
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
