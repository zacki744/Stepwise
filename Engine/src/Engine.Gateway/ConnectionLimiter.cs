namespace Engine.Gateway;

/// <summary>
/// Caps simultaneous connections per client address, so one machine cannot fill the server's session
/// slots. Every connection owns at most one session, so this bounds sessions per address too. Behind a
/// reverse proxy every client shares the proxy's address until forwarded headers are configured.
/// </summary>
public sealed class ConnectionLimiter(int maxPerKey)
{
    private readonly Dictionary<string, int> _counts = [];
    private readonly object _gate = new();

    /// <summary>Takes a slot for this key, or returns null when it is full. Dispose the lease to give the slot back. A limit of 0 or less means no limit.</summary>
    public IDisposable? TryAcquire(string key)
    {
        lock (_gate)
        {
            var current = _counts.GetValueOrDefault(key);
            if (maxPerKey > 0 && current >= maxPerKey) return null;
            _counts[key] = current + 1;
        }
        return new Lease(this, key);
    }

    public int ActiveFor(string key)
    {
        lock (_gate) return _counts.GetValueOrDefault(key);
    }

    private void Release(string key)
    {
        lock (_gate)
        {
            var remaining = _counts.GetValueOrDefault(key) - 1;
            if (remaining <= 0) _counts.Remove(key);
            else _counts[key] = remaining;
        }
    }

    private sealed class Lease(ConnectionLimiter owner, string key) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release(key);
        }
    }
}
