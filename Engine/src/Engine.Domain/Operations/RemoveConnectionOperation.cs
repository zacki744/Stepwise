using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record ConnectionRemovedPayload(int From, int To);

public sealed class RemoveConnectionOperation(int from, int to) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("remove-connection", StateEffect.ReplacesStateAndProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        var original = input.AllDirectedEdges().ToList();
        var remaining = original
            .Where(e => !((e.From == from && e.To == to) || (e.From == to && e.To == from)))
            .ToList();
        bool existed = remaining.Count < original.Count;

        var trace = new TraceRecorder(context);
        trace.Add(existed
            ? new DomainStepEvent("ConnectionRemoved", $"{from} <-> {to}", new ConnectionRemovedPayload(from, to))
            : new DomainStepEvent("ConnectionNotFound", $"No connection between {from} and {to}"));

        return new OperationExecutionResult(true, new Graph(input.NodeCount, remaining), existed, trace, null);
    }
}