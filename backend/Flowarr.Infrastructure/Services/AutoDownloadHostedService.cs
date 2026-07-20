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
    IActiveTransferCancellationRegistry cancellationRegistry,
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

            var seedbox = await connectionSettings.GetSftpSeedboxAsync(cancellationToken);

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

            // SQLite's EF Core provider cannot translate ORDER BY on DateTimeOffset columns, so sort/Take client-side.
            var downloads = (await dbContext.Downloads
                .Where(download =>
                    !string.IsNullOrWhiteSpace(download.RemotePath) &&
                    (download.Status == DownloadStatus.Complete || download.Status == DownloadStatus.Seeding))
                .ToListAsync(cancellationToken))
                .OrderBy(download => download.UpdatedAt)
                .Take(10)
                .ToList();

            foreach (var download in downloads)
            {
                await TryDownloadAsync(dbContext, transferClient, seedbox, autoDownload, download.RemotePath!, download.Id, cancellationToken);
            }

            var importedFiles = (await dbContext.MediaFiles
                .Where(file => file.ImportedLocally && file.ExistsOnSeedbox && !string.IsNullOrWhiteSpace(file.RemotePath))
                .ToListAsync(cancellationToken))
                .OrderByDescending(file => file.LastSeenAt)
                .Take(25)
                .ToList();

            foreach (var file in importedFiles)
            {
                await TryDownloadAsync(dbContext, transferClient, seedbox, autoDownload, file.RemotePath!, null, cancellationToken);
            }

            foreach (var rule in autoDownload.TransferRules.Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath)))
            {
                var sourcePath = GetEffectiveSourcePrefix(seedbox, rule);
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

            var queuedTransfers = (await dbContext.RemoteTransfers
                .Where(transfer => transfer.Status == TransferStatus.Queued)
                .ToListAsync(cancellationToken))
                .OrderBy(transfer => transfer.CreatedAt)
                .Take(3)
                .ToList();

            foreach (var transfer in queuedTransfers)
            {
                var transferRule = FindTransferRule(seedbox, autoDownload, transfer.RemotePath);
                if (transferRule is null)
                {
                    logger.LogInformation("No auto-download transfer rule matched queued transfer {RemotePath}; marking skipped.", transfer.RemotePath);
                    transfer.Status = TransferStatus.Skipped;
                    transfer.Message = "No auto-download transfer rule matched this path.";
                    transfer.CompletedAt = DateTimeOffset.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
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

        var cancelToken = cancellationRegistry.Register(transfer.Id);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancelToken);

        try
        {
            if (transfer.TotalBytes == 0)
            {
                transfer.TotalBytes = await transferClient.GetSizeAsync(transfer.RemotePath, linkedCts.Token);
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

            var result = await transferClient.DownloadAsync(transfer.RemotePath, transferRule.LocalPath, progress, linkedCts.Token);
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
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            transfer.Status = TransferStatus.Cancelled;
            transfer.Message = "Cancelled by user.";
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            DeletePartialLocalContent(transferRule, transfer);

            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "transfer.cancelled",
                TargetType = nameof(RemoteTransfer),
                TargetId = transfer.Id,
                Path = transfer.RemotePath,
                Actor = "user",
                DryRun = false,
                Details = "Transfer cancelled by user; partial local file removed if present."
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
        finally
        {
            cancellationRegistry.Unregister(transfer.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void DeletePartialLocalContent(TransferRuleSettings transferRule, RemoteTransfer transfer)
    {
        // Matches the naming convention SftpSeedboxTransferClient.DownloadDirectory uses for its
        // top-level destination folder/file - best-effort, since the goal is just reclaiming space.
        var candidatePath = Path.Combine(transferRule.LocalPath, Path.GetFileName(transfer.RemotePath.TrimEnd('/')));
        try
        {
            if (Directory.Exists(candidatePath))
            {
                Directory.Delete(candidatePath, recursive: true);
            }
            else if (File.Exists(candidatePath))
            {
                File.Delete(candidatePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete partial local content for cancelled transfer {TransferId} at {Path}", transfer.Id, candidatePath);
        }
    }

    private async Task TryDownloadAsync(
        FlowarrDbContext dbContext,
        IRemoteFileTransferClient transferClient,
        SftpSeedboxConnectionSettings seedbox,
        AutoDownloadConnectionSettings autoDownload,
        string remotePath,
        Guid? downloadId,
        CancellationToken cancellationToken)
    {
        var transferRule = FindTransferRule(seedbox, autoDownload, remotePath);
        if (transferRule is null)
        {
            logger.LogInformation("No auto-download transfer rule matched {RemotePath}", remotePath);
            return;
        }

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
            (transfer.Status == TransferStatus.Queued || transfer.Status == TransferStatus.Running || transfer.Status == TransferStatus.Completed || transfer.Status == TransferStatus.Skipped || transfer.Status == TransferStatus.Cancelled),
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

    private static TransferRuleSettings? FindTransferRule(SftpSeedboxConnectionSettings seedbox, AutoDownloadConnectionSettings settings, string remotePath)
    {
        var rules = settings.TransferRules.Length > 0
            ? settings.TransferRules
            : [new TransferRuleSettings(true, "Default", string.Empty, settings.LocalImportPath, "Download", string.Empty)];

        return rules
            .Where(rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.LocalPath))
            .OrderByDescending(rule => GetEffectiveSourcePrefix(seedbox, rule).Length)
            .FirstOrDefault(rule =>
            {
                var prefix = GetEffectiveSourcePrefix(seedbox, rule);
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

    // A leading "/" in "Remote folder" used to be treated as a literal absolute SFTP path,
    // bypassing the selected seedbox root entirely - but that's not what a user typing "/Movies"
    // means when "Seedbox source root" is "Any Media root". Any leading/trailing slash is now just
    // stripped and the value is always treated as relative to whichever root gets resolved; only
    // when no root can be resolved at all does it fall back to being used as-is.
    private static string GetEffectiveSourcePrefix(SftpSeedboxConnectionSettings seedbox, TransferRuleSettings rule)
    {
        var remotePathPrefix = (rule.RemotePathPrefix ?? string.Empty).Trim().Trim('/');
        var root = ResolveEffectiveRoot(seedbox, rule);

        if (string.IsNullOrWhiteSpace(root))
        {
            return remotePathPrefix;
        }

        return string.IsNullOrWhiteSpace(remotePathPrefix) ? root : $"{root}/{remotePathPrefix}";
    }

    private static string? ResolveEffectiveRoot(SftpSeedboxConnectionSettings seedbox, TransferRuleSettings rule)
    {
        return seedbox.SeedboxRoots
            .FirstOrDefault(root => root.Enabled &&
                (string.IsNullOrWhiteSpace(rule.SourceRootPath)
                    ? root.Kind.Equals(rule.SourceRootKind, StringComparison.OrdinalIgnoreCase)
                    : root.Path.Equals(rule.SourceRootPath, StringComparison.OrdinalIgnoreCase)))
            ?.Path.TrimEnd('/');
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
