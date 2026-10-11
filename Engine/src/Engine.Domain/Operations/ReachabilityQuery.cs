using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// Counts nodes reachable from a given start node via BFS.
/// Read-only: never produces a NewState, only a Value.
/// </summary>
public sealed class ReachabilityQuery(int startNode) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } =
        new("reachability-count", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
		input.EnsureNode(startNode, nameof(startNode));
		var trace = new TraceRecorder(context);
        var visited = new HashSet<int> { startNode };
        var queue = new Queue<int>();
        queue.Enqueue(startNode);

        trace.Add(new DomainStepEvent("NodeVisited", $"Start at {startNode}"));

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (var neighbor in input.GetNeighbors(current))
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                    trace.Add(new DomainStepEvent("NodeVisited", $"Visited {neighbor} via {current}"));
                }
            }
        }

        return new OperationExecutionResult(
            Succeeded: true,
            NewState: null,           // never set for a Query — this is the whole point
            Value: visited.Count,
            Trace: trace,
            FailureReason: null);
    }
}