using Engine.Domain;
using Engine.Domain.Operations;
using System.Collections.Concurrent;

namespace Engine.Session;

public sealed class SessionManager
{
    public const int DefaultMaxSessions = 100;

    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly object _createLock = new();

    public SessionManager(int maxSessions = DefaultMaxSessions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSessions);
        MaxSessions = maxSessions;
    }

    public int MaxSessions { get; }

    /// <param name="replacing">
    /// A session the caller currently owns and is about to discard. If still registered it is swapped
    /// out in the same critical section, so a client at capacity can replace its own session.
    /// </param>
    public Session Create(IDataset dataset, Guid? replacing = null)
    {
        lock (_createLock)
        {
            bool replacingIsRegistered = replacing is Guid r && _sessions.ContainsKey(r);
            if (!replacingIsRegistered && _sessions.Count >= MaxSessions)
                throw new SessionCapacityException(MaxSessions);

            var session = new Session(dataset);
            _sessions[session.Id] = session;
            if (replacing is Guid old) _sessions.TryRemove(old, out _);
            return session;
        }
    }

    public Session? TryGet(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public bool IsIdle(Guid sessionId, TimeSpan threshold)
    {
        var session = TryGet(sessionId);
        if (session is null) return true;
        return DateTime.UtcNow - session.LastActiveUtc > threshold;
    }

    /// <summary>
    /// The session's current dataset, or null if there is no such session. Datasets are never mutated in
    /// place (operations commit a new one), so the caller can read it without holding the session lock.
    /// </summary>
    public IDataset? TryGetDataset(Guid sessionId)
    {
        var session = TryGet(sessionId);
        if (session is null) return null;
        lock (session.SyncRoot)
        {
            session.Touch();
            return session.Dataset;
        }
    }

    public bool Evict(Guid sessionId) => _sessions.TryRemove(sessionId, out _);

    public int ActiveSessionCount => _sessions.Count;

    /// <summary>Runs with no limits. Used by tests and anything that doesn't need a budget.</summary>
    public OperationOutcome Execute<TDataset>(Guid sessionId, IOperation<TDataset> operation)
        where TDataset : class, IDataset
        => Execute(sessionId, operation, OperationContext.Unbounded);

    public OperationOutcome Execute<TDataset>(Guid sessionId, IOperation<TDataset> operation, OperationContext context)
        where TDataset : class, IDataset
    {
        var session = TryGet(sessionId)
            ?? throw new InvalidOperationException($"No session {sessionId}");

        lock (session.SyncRoot)
        {
            if (session.Dataset is not TDataset typedInput)
                throw new DatasetMismatchException(session.Dataset.GetType().Name, typeof(TDataset).Name);

            // If this throws (including OperationAbortedException), nothing below runs:
            // an aborted operation can never leave a half-applied state behind.
            var result = operation.Execute(typedInput, context);

            if (!result.Succeeded)
                return new OperationOutcome(false, result.Trace, null, result.FailureReason);

            if (result.NewState is IDataset newDataset)
                session.CommitState(newDataset);
            else
                session.Touch();

            return new OperationOutcome(true, result.Trace, result.Value, null, result.NewState);
        }
    }

    public int EvictIdle(TimeSpan threshold)
    {
        int evicted = 0;
        foreach (var (id, session) in _sessions)
        {
            if (!Monitor.TryEnter(session.SyncRoot)) continue; // busy means not idle
            try
            {
                if (DateTime.UtcNow - session.LastActiveUtc > threshold && _sessions.TryRemove(id, out _))
                    evicted++;
            }
            finally
            {
                Monitor.Exit(session.SyncRoot);
            }
        }
        return evicted;
    }
}