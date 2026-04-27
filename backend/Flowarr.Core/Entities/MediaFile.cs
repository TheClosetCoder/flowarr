namespace Flowarr.Core.Entities;

public sealed class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? RemotePath { get; set; }
    public long SizeBytes { get; set; }
    public bool ExistsLocally { get; set; }
    public bool ExistsOnSeedbox { get; set; }
    public bool ImportedLocally { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
