using Flowarr.Core.Integrations;

namespace Flowarr.Infrastructure.Integrations;

public sealed class MockRadarrClient : IMediaCatalogClient
{
    public Task<IReadOnlyCollection<MediaItemSnapshot>> GetMediaItemsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<MediaItemSnapshot> items =
        [
            new(
                "Example Movie",
                "radarr:1001",
                "Radarr",
                true,
                [
                    new("/media/movies/Example Movie (2025)/Example Movie.mkv", "/seedbox/completed/Example.Movie.2025.1080p/Example.Movie.mkv", 7_516_192_768, true, true, true)
                ])
        ];

        return Task.FromResult(items);
    }
}
