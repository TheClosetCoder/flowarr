using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;

namespace Flowarr.Infrastructure.Integrations;

public sealed class MockDownloadClient : IDownloadClient
{
    public Task<IReadOnlyCollection<DownloadSnapshot>> GetDownloadsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DownloadSnapshot> downloads =
        [
            new("qBittorrent", "Example.Movie.2025.1080p", "mockhash-001", DownloadStatus.Seeding, 1.0m, 7_516_192_768, "/seedbox/completed/Example.Movie.2025.1080p"),
            new("qBittorrent", "Example.Show.S01E01", "mockhash-002", DownloadStatus.Downloading, 0.42m, 1_503_238_553, "/seedbox/incomplete/Example.Show.S01E01")
        ];

        return Task.FromResult(downloads);
    }
}
