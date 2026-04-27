namespace Flowarr.Core.Entities;

public sealed class MediaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string SourceSystem { get; set; } = "Radarr";
    public bool ExistsInLibrary { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<MediaFile> Files { get; set; } = new List<MediaFile>();
}
