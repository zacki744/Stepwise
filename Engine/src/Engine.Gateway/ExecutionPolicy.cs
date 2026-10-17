using Engine.Domain.Operations;
using Microsoft.Extensions.Options;

namespace Engine.Gateway;

/// <summary>Decides when an operation may run (the gate) and under what limits (the context).</summary>
public sealed class ExecutionPolicy : IDisposable
{
    private readonly ExecutionLimitsOptions _limits;
    private readonly SemaphoreSlim _slots;
    private readonly Dictionary<string, int> _heldByClient = [];
    private readonly object _gate = new();

    public ExecutionPolicy(IOptions<ExecutionLimitsOptions> options)
    {
        _limits = options.Value;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_limits.MaxConcurrentOperations);
        _slots = new SemaphoreSlim(_limits.MaxConcurrentOperations);
    }

    /// <summary>
    /// Returns a lease, or null if the client already has its share of slots (refused at once, so a
    /// flood from one address cannot build a queue) or no slot freed up within the queue timeout.
    /// </summary>
    public async Task<IDisposable?> TryEnterAsync(string clientKey, CancellationToken ct)
    {
        bool counted = _limits.MaxConcurrentOperationsPerClient > 0;
        if (counted)
        {
            lock (_gate)
            {
                var held = _heldByClient.GetValueOrDefault(clientKey);
                if (held >= _limits.MaxConcurrentOperationsPerClient) return null;
                _heldByClient[clientKey] = held + 1;
            }
        }

        bool entered = false;
        try
        {
            entered = await _slots.WaitAsync(_limits.QueueTimeout, ct);
        }
        finally
        {
            if (!entered && counted) ReleaseClient(clientKey);
        }
        return entered ? new Lease(this, counted ? clientKey : null) : null;
    }

    private void ReleaseClient(string clientKey)
    {
        lock (_gate)
        {
            var remaining = _heldByClient.GetValueOrDefault(clientKey) - 1;
            if (remaining <= 0) _heldByClient.Remove(clientKey);
            else _heldByClient[clientKey] = remaining;
        }
    }

    public OperationContext CreateContext(CancellationToken ct) =>
        new(ct, _limits.OperationTimeLimit, _limits.MaxWorkUnits);

    public void Dispose() => _slots.Dispose();

    private sealed class Lease(ExecutionPolicy owner, string? clientKey) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            owner._slots.Release();
            if (clientKey is not null) owner.ReleaseClient(clientKey);
        }
    }
}