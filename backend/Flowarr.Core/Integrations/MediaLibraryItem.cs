namespace Flowarr.Core.Integrations;

/// <summary>
/// One physical file backing a media-server library item (a movie, or a single TV episode),
/// from Plex or Jellyfin. ItemKey is globally unique across servers (clients prefix it, e.g.
/// "ratingkey:123" for Plex, "jellyfin:abc" for Jellyfin). Multiple MediaLibraryItems sharing
/// the same ItemKey mean the server has matched multiple physical copies to the same title -
/// the server's own duplicate-version signal.
/// </summary>
public sealed record MediaLibraryItem(
    string ItemKey,
    string Title,
    string FilePath,
    long FileSizeBytes,
    int ViewCount,
    DateTimeOffset? LastViewedAt,
    DateTimeOffset? AddedAt,
    string MediaType,
    string? SeriesTitle,
    int? SeasonNumber,
    int? EpisodeNumber);
