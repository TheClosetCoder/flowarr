using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Infrastructure.Data;

namespace Flowarr.Infrastructure.Services;

public interface IJobRunService
{
    Task<JobRun> StartAsync(string jobType, CancellationToken cancellationToken = default);
    Task CompleteAsync(JobRun run, CancellationToken cancellationToken = default);
    Task FailAsync(JobRun run, Exception exception, CancellationToken cancellationToken = default);
}

public sealed class JobRunService(FlowarrDbContext dbContext) : IJobRunService
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
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(JobRun run, Exception exception, CancellationToken cancellationToken = default)
    {
        run.Status = exception is OperationCanceledException ? JobRunStatus.Cancelled : JobRunStatus.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ErrorMessage = exception.Message;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
