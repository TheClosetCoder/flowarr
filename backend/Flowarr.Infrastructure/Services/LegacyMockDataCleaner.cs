using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Infrastructure.Services;

public static class LegacyMockDataCleaner
{
    public static async Task RemoveAsync(FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        await RemoveDownloadsAsync(dbContext, cancellationToken);
        await RemoveMediaAsync(dbContext, cancellationToken);
    }

    private static async Task RemoveDownloadsAsync(FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        var legacyDownloads = await dbContext.Downloads
            .Where(download =>
                download.Client == "qBittorrent" &&
                ((download.Name == "Example.Show.S01E01" && download.RemotePath == "/seedbox/incomplete/Example.Show.S01E01") ||
                 (download.Name == "Example.Movie.2025.1080p" && download.RemotePath == "/seedbox/completed/Example.Movie.2025.1080p")))
            .ToListAsync(cancellationToken);

        dbContext.Downloads.RemoveRange(legacyDownloads);
    }

    private static async Task RemoveMediaAsync(FlowarrDbContext dbContext, CancellationToken cancellationToken)
    {
        var legacyMediaItems = await dbContext.MediaItems
            .Include(item => item.Files)
            .Where(item =>
                item.Title == "Example Movie" ||
                item.ExternalId == "radarr-1" ||
                item.Files.Any(file =>
                    file.Path.Contains("Example Movie") ||
                    file.Path.Contains("Example.Movie") ||
                    (file.RemotePath != null &&
                     (file.RemotePath.Contains("Example Movie") || file.RemotePath.Contains("Example.Movie")))))
            .ToListAsync(cancellationToken);

        var mediaFileIds = legacyMediaItems
            .SelectMany(item => item.Files)
            .Select(file => file.Id)
            .ToList();

        var legacyCleanupCandidates = await dbContext.CleanupCandidates
            .Where(candidate =>
                (candidate.MediaFileId != null && mediaFileIds.Contains(candidate.MediaFileId.Value)) ||
                candidate.Path.Contains("Example Movie") ||
                candidate.Path.Contains("Example.Movie"))
            .ToListAsync(cancellationToken);

        dbContext.CleanupCandidates.RemoveRange(legacyCleanupCandidates);
        dbContext.MediaFiles.RemoveRange(legacyMediaItems.SelectMany(item => item.Files));
        dbContext.MediaItems.RemoveRange(legacyMediaItems);
    }
}
