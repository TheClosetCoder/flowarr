namespace Flowarr.Core.Integrations;

/// <summary>
/// One physical file backing a Plex library item (a movie, or a single TV episode).
/// An item with more than one PlexLibraryItem sharing the same RatingKey means Plex has
/// matched multiple physical copies to the same title - Plex's own duplicate-version signal.
/// </summary>
public sealed record PlexLibraryItem(
    string RatingKey,
    string Title,
    string FilePath,
    long FileSizeBytes,
    int ViewCount,
    DateTimeOffset? LastViewedAt,
    DateTimeOffset? AddedAt);
