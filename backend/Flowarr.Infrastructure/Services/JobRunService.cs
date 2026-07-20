using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Services;

public interface IJobRunService
{
    Task<JobRun> StartAsync(string jobType, CancellationToken cancellationToken = default);
    Task CompleteAsync(JobRun run, CancellationToken cancellationToken = default);
    Task FailAsync(JobRun run, Exception exception, CancellationToken cancellationToken = default);
    Task ReportProgressAsync(JobRun run, string message, CancellationToken cancellationToken = default);
}

public sealed class JobRunService(FlowarrDbContext dbContext, ILogger<JobRunService> logger) : IJobRunService
{
    public async Task<JobRun> StartAsync(string jobType, CancellationToken cancellationToken = default)
    {
        var run = new JobRun { JobType = jobType, Status = JobRunStatus.Running };
        dbContext.JobRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task CompleteAsync(JobRun run, CancellationToken cancellationToken = default)
    {
        run.Status = JobRunStatus.Succeeded;
        run.CompletedAt = DateTimeOffset.UtcNow;
        await SaveBestEffortAsync(run, cancellationToken);
    }

    public async Task FailAsync(JobRun run, Exception exception, CancellationToken cancellationToken = default)
    {
        run.Status = exception is OperationCanceledException ? JobRunStatus.Cancelled : JobRunStatus.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ErrorMessage = exception.Message;
        await SaveBestEffortAsync(run, cancellationToken);
    }

    public async Task ReportProgressAsync(JobRun run, string message, CancellationToken cancellationToken = default)
    {
        run.Progress = message;
        await SaveBestEffortAsync(run, cancellationToken);
    }

    // Recording a job's outcome/progress is inherently best-effort bookkeeping - if it fails (e.g. a
    // transient SQLite contention error), that must never cascade into a bigger failure. Callers
    // (background services in particular) would otherwise have an exception thrown from their own
    // *error-handling* path go unhandled, which for a BackgroundService crashes the entire host.
    private async Task SaveBestEffortAsync(JobRun run, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist JobRun {JobRunId} status update", run.Id);
        }
    }
}
