using Flowarr.Core.Enums;

namespace Flowarr.Core.Entities;

public sealed class StorageLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public StorageKind Kind { get; set; }
    public StorageCategory Category { get; set; }
    public string PathOrEndpoint { get; set; } = string.Empty;
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public DateTimeOffset LastScannedAt { get; set; } = DateTimeOffset.UtcNow;
}
