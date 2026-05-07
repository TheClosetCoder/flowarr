using Flowarr.Core.Enums;

namespace Flowarr.Core.Entities;

public sealed class RemoteTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DownloadId { get; set; }
    public Download? Download { get; set; }
    public string Provider { get; set; } = "SFTP";
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Queued;
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
