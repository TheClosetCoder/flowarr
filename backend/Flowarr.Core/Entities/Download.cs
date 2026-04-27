using Flowarr.Core.Enums;

namespace Flowarr.Core.Entities;

public sealed class Download
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Client { get; set; } = "qBittorrent";
    public string Name { get; set; } = string.Empty;
    public string? Hash { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Unknown;
    public decimal Progress { get; set; }
    public long SizeBytes { get; set; }
    public string? RemotePath { get; set; }
    public Guid? MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
