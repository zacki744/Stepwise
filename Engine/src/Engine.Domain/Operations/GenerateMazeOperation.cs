using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// Randomized DFS (recursive backtracker) maze generation over a width×height
/// grid, treated as a graph. Produces a spanning tree — exactly one path
/// between any two cells, no loops. Seeded for determinism (required for
/// rehydration replay to produce an identical maze).
/// </summary>
public sealed class GenerateMazeOperation(int width, int height, int seed) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("generate-maze", StateEffect.ReplacesState);
    public sealed record PassageCarvedPayload(int From, int To);
    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        DatasetLimits.EnsureWithinLimit((long)width * height, $"Maze {width}x{height}");

        int nodeCount = width * height;
        var random = new Random(seed);
        var adjacency = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++) adjacency[i] = [];

        var visited = new bool[nodeCount];
        var trace = new TraceRecorder(context);
        var stack = new Stack<int>();

        visited[0] = true;
        stack.Push(0);

        while (stack.Count > 0)
        {
            int current = stack.Peek();
            var unvisitedNeighbors = GetGridNeighbors(current, width, height)
                .Where(n => !visited[n])
                .ToList();

            if (unvisitedNeighbors.Count == 0)
            {
                stack.Pop(); // dead end — backtrack
                continue;
            }

            int next = unvisitedNeighbors[random.Next(unvisitedNeighbors.Count)];
            visited[next] = true;

            adjacency[current].Add(next);
            adjacency[next].Add(current); // undirected passage

            trace.Add(new DomainStepEvent("PassageCarved", $"{current} <-> {next}", new PassageCarvedPayload(current, next)));
            stack.Push(next);
        }

        var maze = BuildGraph(nodeCount, adjacency);

        return new OperationExecutionResult(
            Succeeded: true,
            NewState: maze,
            Value: null,
            Trace: trace,
            FailureReason: null);
    }

    private static Graph BuildGraph(int nodeCount, List<int>[] adjacency)
    {
        // Graph's edges are directed pairs internally; represent each carved
        // passage in both directions so GetNeighbors works from either end.
        var edges = new List<(int, int)>();
        for (int node = 0; node < nodeCount; node++)
            foreach (var neighbor in adjacency[node])
                edges.Add((node, neighbor));

        return new Graph(nodeCount, edges);
    }

    private static IEnumerable<int> GetGridNeighbors(int node, int width, int height)
    {
        int row = node / width;
        int col = node % width;

        if (row > 0) yield return node - width;
        if (row < height - 1) yield return node + width;
        if (col > 0) yield return node - 1;
        if (col < width - 1) yield return node + 1;
    }
}