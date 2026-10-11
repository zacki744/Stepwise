using System.Diagnostics;

namespace Engine.Domain.Operations;

public enum OperationAbortReason { Cancelled, TimeLimitExceeded, WorkBudgetExceeded }

/// <summary>
/// Thrown by OperationContext.Checkpoint. Safe by construction: operations work on copies and the
/// session only commits after Execute returns, so an aborted operation changes nothing.
/// </summary>
public sealed class OperationAbortedException(OperationAbortReason reason, string message) : Exception(message)
{
    public OperationAbortReason Reason { get; } = reason;
}

/// <summary>
/// Cooperative limits for one operation run. Operations call Checkpoint() inside any loop that can
/// do super-linear work; every step recorded in a TraceRecorder also counts as one unit, so traced
/// operations are interruptible without extra code. NOT thread-safe: one context per run.
/// </summary>
public sealed class OperationContext
{
    // Reading the clock and token costs more than incrementing a counter, so look every ~1k units.
    private const long ClockCheckInterval = 1_024;

    private readonly CancellationToken _cancellation;
    private readonly long _deadlineTimestamp;
    private readonly long _maxWorkUnits;
    private long _workUnits;
    private long _unitsSinceClockCheck;

    public OperationContext(CancellationToken cancellation, TimeSpan? timeLimit, long maxWorkUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxWorkUnits);
        _cancellation = cancellation;
        _maxWorkUnits = maxWorkUnits;
        _deadlineTimestamp = timeLimit is { } limit
            ? Stopwatch.GetTimestamp() + (long)(limit.TotalSeconds * Stopwatch.Frequency)
            : long.MaxValue;
    }

    /// <summary>A fresh no-limits context each call (contexts carry mutable counters, so never shared).</summary>
    public static OperationContext Unbounded => new(CancellationToken.None, timeLimit: null, maxWorkUnits: long.MaxValue);

    public void Checkpoint(long units = 1)
    {
        _workUnits += units;
        if (_workUnits > _maxWorkUnits)
            throw new OperationAbortedException(OperationAbortReason.WorkBudgetExceeded,
                $"Operation exceeded its work budget of {_maxWorkUnits:N0} steps");

        _unitsSinceClockCheck += units;
        if (_unitsSinceClockCheck < ClockCheckInterval) return;
        _unitsSinceClockCheck = 0;

        if (_cancellation.IsCancellationRequested)
            throw new OperationAbortedException(OperationAbortReason.Cancelled, "Operation was cancelled");
        if (Stopwatch.GetTimestamp() >= _deadlineTimestamp)
            throw new OperationAbortedException(OperationAbortReason.TimeLimitExceeded, "Operation exceeded its time limit");
    }
}