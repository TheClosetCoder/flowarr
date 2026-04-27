using Flowarr.Core.Enums;

namespace Flowarr.Core.Integrations;

public sealed record DownloadSnapshot(
    string Client,
    string Name,
    string? Hash,
    DownloadStatus Status,
    decimal Progress,
    long SizeBytes,
    string? RemotePath);
