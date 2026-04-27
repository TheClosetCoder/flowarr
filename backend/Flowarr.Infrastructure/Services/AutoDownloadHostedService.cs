using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Services;

public sealed class AutoDownloadHostedService(
    IServiceScopeFactory scopeFactory,
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
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
            var transferClient = scope.ServiceProvider.GetRequiredService<IRemoteFileTransferClient>();
            var connectionSettings = scope.ServiceProvider.GetRequiredService<IConnectionSettingsService>();
            var autoDownload = await connectionSettings.GetAutoDownloadAsync(cancellationToken);

            if (!autoDownload.Enabled)
            {
                return;
            }

            if (!await transferClient.IsConfiguredAsync(cancellationToken))
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

                var transferRule = FindTransferRule(autoDownload, download.RemotePath!);
                if (transferRule is null)
                {
                    logger.LogInformation("No auto-download transfer rule matched {RemotePath}", download.RemotePath);
                    continue;
                }

                await DownloadAsync(dbContext, transferClient, autoDownload, transferRule, download, cancellationToken);
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
        AutoDownloadConnectionSettings autoDownload,
        TransferRuleSettings transferRule,
        Download download,
        CancellationToken cancellationToken)
    {
        var transfer = new RemoteTransfer
        {
            DownloadId = download.Id,
            Provider = "SFTP",
            RemotePath = download.RemotePath!,
            LocalPath = transferRule.LocalPath,
            Status = TransferStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        dbContext.RemoteTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await transferClient.DownloadAsync(download.RemotePath!, transferRule.LocalPath, cancellationToken);
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

    private static TransferRuleSettings? FindTransferRule(AutoDownloadConnectionSettings settings, string remotePath)
    {
        var rules = settings.TransferRules.Length > 0
            ? settings.TransferRules
            : [new TransferRuleSettings(true, "Default", string.Empty, settings.LocalImportPath)];

        return rules
            .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath))
            .OrderByDescending(rule => rule.RemotePathPrefix?.Length ?? 0)
            .FirstOrDefault(rule =>
                string.IsNullOrWhiteSpace(rule.RemotePathPrefix) ||
                remotePath.StartsWith(rule.RemotePathPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }
}
