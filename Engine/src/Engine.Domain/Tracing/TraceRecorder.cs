using System.Collections;
using Engine.Domain.Operations;

namespace Engine.Domain.Tracing;

/// <summary>
/// Collects an operation's trace with bounded memory: the first DefaultMaxEvents events are kept in
/// full, and every event beyond that is only counted (in total and per step type). It IS the list
/// handed back as OperationExecutionResult.Trace.
///
/// Count is the number of RETAINED events; TotalCount is how many actually happened. To ask "how many
/// steps happened" of any trace, recorder or plain list, use TraceSummary.Of(trace).
/// If given an OperationContext, every Add counts as one unit of work.
/// Not thread-safe: an operation runs on one thread, under its session's lock.
/// </summary>
public sealed class TraceRecorder : IReadOnlyList<DomainStepEvent>
{
    public const int DefaultMaxEvents = 2_000;

    private readonly OperationContext? _context;
    private readonly int _maxEvents;
    private readonly List<DomainStepEvent> _events = [];
    private readonly Dictionary<string, int> _countsByType = [];

    public TraceRecorder(OperationContext? context = null, int maxEvents = DefaultMaxEvents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEvents);
        _context = context;
        _maxEvents = maxEvents;
    }

    public int TotalCount { get; private set; }
    public bool IsTruncated => TotalCount > _events.Count;
    public TraceSummary Summary => new(TotalCount, new Dictionary<string, int>(_countsByType));

    public void Add(DomainStepEvent step)
    {
        _context?.Checkpoint();
        TotalCount++;
        _countsByType[step.StepType] = _countsByType.GetValueOrDefault(step.StepType) + 1;
        if (_events.Count < _maxEvents) _events.Add(step);
    }

    public int Count => _events.Count;
    public DomainStepEvent this[int index] => _events[index];
    public IEnumerator<DomainStepEvent> GetEnumerator() => _events.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}