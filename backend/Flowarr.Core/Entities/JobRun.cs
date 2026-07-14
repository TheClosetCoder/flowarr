using Flowarr.Core.Enums;

namespace Flowarr.Core.Entities;

public sealed class JobRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JobType { get; set; } = string.Empty;
    public JobRunStatus Status { get; set; } = JobRunStatus.Running;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Progress { get; set; }
}
