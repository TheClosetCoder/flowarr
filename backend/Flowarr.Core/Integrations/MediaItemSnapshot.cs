namespace Flowarr.Core.Integrations;

public sealed record MediaItemSnapshot(
    string Title,
    string? ExternalId,
    string SourceSystem,
    bool ExistsInLibrary,
    IReadOnlyCollection<MediaFileSnapshot> Files);

public sealed record MediaFileSnapshot(
    string Path,
    string? RemotePath,
    long SizeBytes,
    bool ExistsLocally,
    bool ExistsOnSeedbox,
    bool ImportedLocally);
