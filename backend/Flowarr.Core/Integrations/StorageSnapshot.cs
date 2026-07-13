using Flowarr.Core.Enums;

namespace Flowarr.Core.Integrations;

public sealed record StorageSnapshot(
    string Name,
    StorageKind Kind,
    string PathOrEndpoint,
    long UsedBytes,
    long FreeBytes,
    StorageCategory Category);
