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

            // A transfer can only be genuinely "Running" while this method's download loop below is
            // awaiting it; since ExecuteAsync never overlaps RunOnceAsync calls, any Running row found
            // here at the start of a cycle is orphaned from a process that died mid-download. Requeue it.
            var orphanedTransfers = await dbContext.RemoteTransfers
                .Where(transfer => transfer.Status == TransferStatus.Running)
                .ToListAsync(cancellationToken);

            foreach (var orphaned in orphanedTransfers)
            {
                orphaned.Status = TransferStatus.Queued;
                orphaned.Message = "Requeued after restart; previous attempt did not complete.";
            }

            if (orphanedTransfers.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
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
                await TryDownloadAsync(dbContext, transferClient, connectionSettings, autoDownload, download.RemotePath!, download.Id, cancellationToken);
            }

            var importedFiles = await dbContext.MediaFiles
                .Where(file => file.ImportedLocally && file.ExistsOnSeedbox && !string.IsNullOrWhiteSpace(file.RemotePath))
                .OrderByDescending(file => file.LastSeenAt)
                .Take(25)
                .ToListAsync(cancellationToken);

            foreach (var file in importedFiles)
            {
                await TryDownloadAsync(dbContext, transferClient, connectionSettings, autoDownload, file.RemotePath!, null, cancellationToken);
            }

            foreach (var rule in autoDownload.TransferRules.Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath)))
            {
                var sourcePath = GetEffectiveSourcePrefix(rule);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    continue;
                }

                var entries = await transferClient.ListAsync(sourcePath, cancellationToken);
                foreach (var entry in entries.Take(100))
                {
                    await GetOrQueueTransferAsync(dbContext, transferRule: rule, remotePath: entry.RemotePath, downloadId: null, totalBytes: entry.SizeBytes, cancellationToken);
                }
            }

            var queuedTransfers = await dbContext.RemoteTransfers
                .Where(transfer => transfer.Status == TransferStatus.Queued)
                .OrderBy(transfer => transfer.CreatedAt)
                .Take(3)
                .ToListAsync(cancellationToken);

            foreach (var transfer in queuedTransfers)
            {
                var transferRule = FindTransferRule(autoDownload, transfer.RemotePath);
                if (transferRule is null)
                {
                    transfer.Status = TransferStatus.Skipped;
                    transfer.Message = "No auto-download transfer rule matched this path.";
                    transfer.CompletedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                await DownloadAsync(dbContext, transferClient, autoDownload, transferRule, transfer, cancellationToken);
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
        RemoteTransfer transfer,
        CancellationToken cancellationToken)
    {
        transfer.LocalPath = transferRule.LocalPath;
        transfer.Status = TransferStatus.Running;
        transfer.StartedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            if (transfer.TotalBytes == 0)
            {
                transfer.TotalBytes = await transferClient.GetSizeAsync(transfer.RemotePath, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var progress = new InlineProgress<long>(bytes =>
            {
                transfer.BytesTransferred += bytes;

                // The SFTP client reports progress per chunk (can be hundreds of times per file),
                // so only persist to the database periodically rather than on every tick.
                if (progressStopwatch.Elapsed < TimeSpan.FromSeconds(1))
                {
                    return;
                }

                progressStopwatch.Restart();
                dbContext.SaveChanges();
            });

            var result = await transferClient.DownloadAsync(transfer.RemotePath, transferRule.LocalPath, progress, cancellationToken);
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

            if (!result.Skipped && transferRule.DeleteRemoteAfterDownload)
            {
                dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "remote.delete.requested",
                    TargetType = nameof(RemoteTransfer),
                    TargetId = transfer.Id,
                    Path = result.RemotePath,
                    Actor = "auto-download",
                    DryRun = false,
                    Details = "Remote delete requested by download folder rule after successful download."
                });
                await dbContext.SaveChangesAsync(cancellationToken);

                await transferClient.DeleteAsync(result.RemotePath, cancellationToken);

                dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "remote.delete.completed",
                    TargetType = nameof(RemoteTransfer),
                    TargetId = transfer.Id,
                    Path = result.RemotePath,
                    Actor = "auto-download",
                    DryRun = false,
                    Details = "Remote path deleted after successful download."
                });

                transfer.Message = $"{transfer.Message} Remote source deleted.";
            }
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

    private async Task TryDownloadAsync(
        FlowarrDbContext dbContext,
        IRemoteFileTransferClient transferClient,
        IConnectionSettingsService connectionSettings,
        AutoDownloadConnectionSettings autoDownload,
        string remotePath,
        Guid? downloadId,
        CancellationToken cancellationToken)
    {
        var transferRule = FindTransferRule(autoDownload, remotePath);
        if (transferRule is null)
        {
            logger.LogInformation("No auto-download transfer rule matched {RemotePath}", remotePath);
            return;
        }

        var seedbox = await connectionSettings.GetSftpSeedboxAsync(cancellationToken);
        if (!IsAllowedSourcePath(seedbox, transferRule, remotePath))
        {
            logger.LogWarning("Skipping {RemotePath}; it is outside configured seedbox root {RootPath}.", remotePath, GetRuleRootLabel(transferRule));
            return;
        }

        var totalBytes = await transferClient.GetSizeAsync(remotePath, cancellationToken);
        var transfer = await GetOrQueueTransferAsync(dbContext, transferRule, remotePath, downloadId, totalBytes, cancellationToken);
        if (transfer is null)
        {
            return;
        }

        await DownloadAsync(dbContext, transferClient, autoDownload, transferRule, transfer, cancellationToken);
    }

    /// <summary>
    /// Returns the transfer to (re-)download, or null if nothing new needs to happen.
    /// A prior Completed/Skipped transfer for the same remote path is reused as long as the remote
    /// content's size hasn't changed; if it has (e.g. new files landed in an already-downloaded folder),
    /// the transfer is reset to Queued so the new content gets picked up.
    /// </summary>
    private static async Task<RemoteTransfer?> GetOrQueueTransferAsync(
        FlowarrDbContext dbContext,
        TransferRuleSettings transferRule,
        string remotePath,
        Guid? downloadId,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.RemoteTransfers.FirstOrDefaultAsync(transfer =>
            transfer.RemotePath == remotePath &&
            (transfer.Status == TransferStatus.Queued || transfer.Status == TransferStatus.Running || transfer.Status == TransferStatus.Completed || transfer.Status == TransferStatus.Skipped),
            cancellationToken);

        if (existing is not null)
        {
            if (existing.Status is TransferStatus.Queued or TransferStatus.Running)
            {
                return null;
            }

            if (totalBytes == existing.TotalBytes)
            {
                return null;
            }

            existing.Status = TransferStatus.Queued;
            existing.TotalBytes = totalBytes;
            existing.BytesTransferred = 0;
            existing.LocalPath = transferRule.LocalPath;
            existing.Message = "Requeued by auto-download scan; remote content changed.";
            existing.StartedAt = null;
            existing.CompletedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var transfer = new RemoteTransfer
        {
            DownloadId = downloadId,
            Provider = "SFTP",
            RemotePath = remotePath,
            LocalPath = transferRule.LocalPath,
            TotalBytes = totalBytes,
            Status = TransferStatus.Queued,
            Message = "Queued by auto-download scan."
        };

        dbContext.RemoteTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return transfer;
    }

    private static TransferRuleSettings? FindTransferRule(AutoDownloadConnectionSettings settings, string remotePath)
    {
        var rules = settings.TransferRules.Length > 0
            ? settings.TransferRules
            : [new TransferRuleSettings(true, "Default", string.Empty, settings.LocalImportPath, "Download", string.Empty)];

        return rules
            .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath))
            .OrderByDescending(rule => GetEffectiveSourcePrefix(rule).Length)
            .FirstOrDefault(rule =>
            {
                var prefix = GetEffectiveSourcePrefix(rule);
                return string.IsNullOrWhiteSpace(prefix) ||
                       remotePath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                       remotePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static bool IsAllowedSourcePath(SftpSeedboxConnectionSettings seedbox, TransferRuleSettings rule, string remotePath)
    {
        var roots = seedbox.SeedboxRoots
            .Where(root => root.Enabled &&
                           (string.IsNullOrWhiteSpace(rule.SourceRootPath)
                               ? root.Kind.Equals(rule.SourceRootKind, StringComparison.OrdinalIgnoreCase)
                               : root.Path.Equals(rule.SourceRootPath, StringComparison.OrdinalIgnoreCase)))
            .Select(root => root.Path.TrimEnd('/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        if (roots.Count == 0)
        {
            return true;
        }

        return roots.Any(root => remotePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                                 remotePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetEffectiveSourcePrefix(TransferRuleSettings rule)
    {
        var remotePathPrefix = rule.RemotePathPrefix?.Trim() ?? string.Empty;
        if (remotePathPrefix.StartsWith("/", StringComparison.Ordinal))
        {
            return remotePathPrefix.TrimEnd('/');
        }

        var sourceRoot = rule.SourceRootPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return remotePathPrefix.TrimEnd('/');
        }

        return string.IsNullOrWhiteSpace(remotePathPrefix)
            ? sourceRoot.TrimEnd('/')
            : $"{sourceRoot.TrimEnd('/')}/{remotePathPrefix.TrimStart('/').TrimEnd('/')}";
    }

    private static string GetRuleRootLabel(TransferRuleSettings rule)
    {
        return string.IsNullOrWhiteSpace(rule.SourceRootPath) ? rule.SourceRootKind : rule.SourceRootPath;
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }
}
