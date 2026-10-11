using System.Text.Json;
using Engine.Domain.Tracing;
using Engine.Protocol;

namespace Engine.Gateway;

public static class TraceStreamingPolicy
{
    public const int MaxStreamableSteps = TraceRecorder.DefaultMaxEvents;

    public static bool WasCoalesced(IReadOnlyList<DomainStepEvent> trace) =>
        IsCoalesced(TraceSummary.Of(trace));

    private static bool IsCoalesced(TraceSummary summary) => summary.TotalCount > MaxStreamableSteps;

    public static IEnumerable<StepEventMessage> BuildMessages(IReadOnlyList<DomainStepEvent> trace)
    {
        var summary = TraceSummary.Of(trace);

        if (!IsCoalesced(summary))
        {
            foreach (var step in trace)
                yield return new StepEventMessage(step.StepType, step.Description,
                    step.Payload is not null ? JsonSerializer.SerializeToElement(step.Payload, ProtocolJson.Options) : null);
            yield break;
        }

        yield return new StepEventMessage(
            "TraceCoalesced",
            $"{summary.TotalCount} steps exceeded the {MaxStreamableSteps}-step streaming limit — coalesced into this summary",
            JsonSerializer.SerializeToElement(
                new { totalSteps = summary.TotalCount, countsByType = summary.CountsByType },
                ProtocolJson.Options));
    }
}