namespace Engine.Gateway;

/// <summary>
/// Remembers the last few request ids a connection used. A client that retries executeOperation with the
/// same requestId (after a timeout, say) must not apply a non-idempotent operation twice, so a repeat is
/// refused and the client is told to check the state. Bounded; not thread-safe (one connection, one reader).
/// </summary>
public sealed class RecentRequestIds(int capacity = 1_024)
{
    private readonly HashSet<Guid> _seen = [];
    private readonly Queue<Guid> _order = new();

    /// <summary>True the first time an id is seen; false for a repeat still within the window.</summary>
    public bool TryRecord(Guid id)
    {
        if (!_seen.Add(id)) return false;
        _order.Enqueue(id);
        if (_order.Count > capacity) _seen.Remove(_order.Dequeue());
        return true;
    }
}
