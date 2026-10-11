namespace Engine.Domain.Tracing;

/// <summary>What actually happened during an operation, even if not every event was retained.</summary>
public sealed record TraceSummary(int TotalCount, IReadOnlyDictionary<string, int> CountsByType)
{
    public static TraceSummary Of(IReadOnlyList<DomainStepEvent> trace) =>
        trace is TraceRecorder recorder
            ? recorder.Summary
            : new TraceSummary(trace.Count, trace.GroupBy(e => e.StepType).ToDictionary(g => g.Key, g => g.Count()));
}