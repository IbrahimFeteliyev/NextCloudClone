using System.Collections.Concurrent;
namespace Atlas.Api.Services;
// The local demo runs one API process. EF also checks the content version on updates.
public sealed class DocumentLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();
    public async Task<IDisposable> Enter(Guid file, CancellationToken cancellationToken = default)
    {
        var gate = gates.GetOrAdd(file, _ => new(1, 1)); await gate.WaitAsync(cancellationToken); return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }
}
