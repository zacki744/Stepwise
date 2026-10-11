using Engine.Domain.Operations;
using Microsoft.Extensions.Options;

namespace Engine.Gateway;

/// <summary>Decides when an operation may run (the gate) and under what limits (the context).</summary>
public sealed class ExecutionPolicy : IDisposable
{
    private readonly ExecutionLimitsOptions _limits;
    private readonly SemaphoreSlim _slots;

    public ExecutionPolicy(IOptions<ExecutionLimitsOptions> options)
    {
        _limits = options.Value;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_limits.MaxConcurrentOperations);
        _slots = new SemaphoreSlim(_limits.MaxConcurrentOperations);
    }

    /// <summary>Returns a lease, or null if no slot freed up within the queue timeout.</summary>
    public async Task<IDisposable?> TryEnterAsync(CancellationToken ct)
    {
        if (!await _slots.WaitAsync(_limits.QueueTimeout, ct)) return null;
        return new Lease(_slots);
    }

    public OperationContext CreateContext(CancellationToken ct) =>
        new(ct, _limits.OperationTimeLimit, _limits.MaxWorkUnits);

    public void Dispose() => _slots.Dispose();

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}