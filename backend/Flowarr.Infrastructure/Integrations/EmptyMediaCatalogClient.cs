using Flowarr.Core.Integrations;

namespace Flowarr.Infrastructure.Integrations;

public sealed class EmptyMediaCatalogClient : IMediaCatalogClient
{
    public Task<IReadOnlyCollection<MediaItemSnapshot>> GetMediaItemsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<MediaItemSnapshot>>([]);
    }
}
