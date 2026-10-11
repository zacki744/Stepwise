using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record ComponentFoundPayload(int ComponentIndex, IReadOnlyList<int> Nodes);

/// <summary>
/// Groups nodes into connected components via BFS from each unvisited node.
/// Treats edges as effectively undirected for reachability purposes (same
/// assumption as the rest of the network-mapping operations) — a node
/// reachable from another in either direction counts as the same component.
/// A fully connected network reports exactly one component, covering every
/// node — the baseline "no partition" case a client can check for directly.
/// </summary>
public sealed class FindConnectedComponentsOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("find-connected-components", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        var visited = new bool[n];
        var trace = new TraceRecorder(context);
        var components = new List<List<int>>();

        for (int start = 0; start < n; start++)
        {
            if (visited[start]) continue;

            var component = new List<int>();
            var queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                component.Add(node);
                foreach (var neighbor in input.GetNeighbors(node))
                {
                    if (visited[neighbor]) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            component.Sort();
            components.Add(component);
            trace.Add(new DomainStepEvent("ComponentFound",
                $"Component {components.Count - 1}: [{string.Join(",", component)}]",
                new ComponentFoundPayload(components.Count - 1, component)));
        }

        return new OperationExecutionResult(true, null, components, trace, null);
    }
}