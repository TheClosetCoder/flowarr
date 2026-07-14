using System.Collections.Concurrent;

namespace Flowarr.Infrastructure.Services;

public interface IActiveTransferCancellationRegistry
{
    CancellationToken Register(Guid transferId);
    bool Cancel(Guid transferId);
    void Unregister(Guid transferId);
}

/// <summary>
/// Tracks a CancellationTokenSource per in-flight transfer so a user-initiated cancel (an API
/// request, in its own scope) can interrupt a download running inside the singleton background
/// hosted service. Deliberately independent of the hosted service's own shutdown token - app
/// shutdown continues to rely on the orphaned-Running-transfer recovery on next startup instead.
/// </summary>
public sealed class ActiveTransferCancellationRegistry : IActiveTransferCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    public CancellationToken Register(Guid transferId)
    {
        var cts = new CancellationTokenSource();
        _sources[transferId] = cts;
        return cts.Token;
    }

    public bool Cancel(Guid transferId)
    {
        if (_sources.TryGetValue(transferId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    public void Unregister(Guid transferId)
    {
        if (_sources.TryRemove(transferId, out var cts))
        {
            cts.Dispose();
        }
    }
}
