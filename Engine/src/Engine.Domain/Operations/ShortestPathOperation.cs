using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeSettledPayload(int Node, double Distance);
public sealed record ShortestPathResult(IReadOnlyList<int> Path, double TotalDistance);

/// <summary>
/// Dijkstra's algorithm. Assumes non-negative edge weights — not validated;
/// negative weights will silently produce an incorrect result, a known,
/// documented limitation rather than a handled one.
/// Deterministic: frontier ties are broken by ascending node id, since a
/// plain priority queue does not guarantee tie order and would otherwise
/// break rehydration replay.
/// </summary>
public sealed class ShortestPathOperation(int start, int target) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("shortest-path", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
		input.EnsureNode(start, nameof(start));
		input.EnsureNode(target, nameof(target));
		foreach (var (_, _, weight) in input.AllDirectedEdges())
			if (!double.IsFinite(weight) || weight < 0)
				throw new ArgumentException("shortest-path (Dijkstra) needs finite, non-negative edge weights");

		int n = input.NodeCount;
        var distance = new double[n];
        var previous = new int[n];
        var visited = new bool[n];
        Array.Fill(distance, double.PositiveInfinity);
        Array.Fill(previous, -1);
        distance[start] = 0;

        var trace = new TraceRecorder(context);
        var frontier = new SortedSet<(double Dist, int Node)>(
            Comparer<(double Dist, int Node)>.Create((a, b) =>
                a.Dist != b.Dist ? a.Dist.CompareTo(b.Dist) : a.Node.CompareTo(b.Node)));
        frontier.Add((0, start));

        while (frontier.Count > 0)
        {
            var (dist, node) = frontier.Min;
            frontier.Remove(frontier.Min);
            if (visited[node]) continue;
            visited[node] = true;

            trace.Add(new DomainStepEvent("NodeSettled", $"Settled {node} at distance {dist}",
                new NodeSettledPayload(node, dist)));

            if (node == target) break;

            foreach (var (neighbor, weight) in input.GetWeightedNeighbors(node))
            {
                if (visited[neighbor]) continue;
                double candidate = dist + weight;
                if (candidate < distance[neighbor])
                {
                    distance[neighbor] = candidate;
                    previous[neighbor] = node;
                    frontier.Add((candidate, neighbor));
                }
            }
        }

        if (double.IsPositiveInfinity(distance[target]))
        {
            trace.Add(new DomainStepEvent("NoPathFound", $"No path from {start} to {target}"));
            return new OperationExecutionResult(true, null, null, trace, null);
        }

        var path = new List<int>();
        for (int at = target; at != -1; at = previous[at]) path.Add(at);
        path.Reverse();

        var pathResult = new ShortestPathResult(path, distance[target]);
        trace.Add(new DomainStepEvent("PathFound", $"Path found, distance {distance[target]}", pathResult));

        return new OperationExecutionResult(true, null, pathResult, trace, null);
    }
}