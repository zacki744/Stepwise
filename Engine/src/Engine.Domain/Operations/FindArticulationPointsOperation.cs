using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeVisitedPayload(int Node, int DiscoveryTime);
public sealed record ArticulationPointFoundPayload(int Node);

/// <summary>
/// Single points of failure: nodes whose removal disconnects part of the network.
/// Iterative (see LowLinkAnalysis), so graph shape cannot overflow the stack.
/// </summary>
public sealed class FindArticulationPointsOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("find-articulation-points", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        var analysis = new LowLinkAnalysis(input, context);
        var trace = new TraceRecorder(context);

        foreach (var (node, time) in analysis.VisitOrder)
            trace.Add(new DomainStepEvent("NodeVisited", $"Visited {node} at time {time}",
                new NodeVisitedPayload(node, time)));

        var points = new List<int>();
        for (int i = 0; i < input.NodeCount; i++)
        {
            if (!analysis.IsArticulation[i]) continue;
            points.Add(i);
            trace.Add(new DomainStepEvent("ArticulationPointFound", $"Node {i} is a single point of failure",
                new ArticulationPointFoundPayload(i)));
        }

        return new OperationExecutionResult(true, null, points, trace, null);
    }
}