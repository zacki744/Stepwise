using Engine.Domain.Datasets;

namespace Engine.Domain.Operations;

/// <summary>
/// A symmetric view of a graph: every stored edge a -> b appears in both a's and b's neighbour list
/// (a self-loop appears twice in its own list). Used by operations whose answer is a property of the
/// UNDIRECTED structure, so they agree however connections happen to be stored. Built in O(V + E).
/// </summary>
internal sealed class UndirectedAdjacency
{
    private readonly int[] _offsets;
    private readonly int[] _targets;

    public UndirectedAdjacency(Graph graph)
    {
        NodeCount = graph.NodeCount;
        _offsets = new int[NodeCount + 1];
        foreach (var (from, to, _) in graph.AllDirectedEdges())
        {
            _offsets[from + 1]++;
            _offsets[to + 1]++;
        }
        for (int i = 0; i < NodeCount; i++) _offsets[i + 1] += _offsets[i];

        _targets = new int[_offsets[NodeCount]];
        var cursor = new int[NodeCount];
        foreach (var (from, to, _) in graph.AllDirectedEdges())
        {
            _targets[_offsets[from] + cursor[from]++] = to;
            _targets[_offsets[to] + cursor[to]++] = from;
        }
    }

    public int NodeCount { get; }
    public int Degree(int node) => _offsets[node + 1] - _offsets[node];
    public int NeighborAt(int node, int index) => _targets[_offsets[node] + index];
}

internal static class BipartiteColoring
{
    /// <summary>Two-colours the graph by BFS, or returns null colours plus the first monochromatic edge found.</summary>
    public static (int[]? Colors, int ConflictFrom, int ConflictTo) Compute(
        UndirectedAdjacency adjacency, OperationContext context)
    {
        int n = adjacency.NodeCount;
        var color = new int[n];
        Array.Fill(color, -1);
        var queue = new int[n];

        for (int root = 0; root < n; root++)
        {
            if (color[root] != -1) continue;
            color[root] = 0;
            int head = 0, tail = 0;
            queue[tail++] = root;

            while (head < tail)
            {
                int u = queue[head++];
                for (int i = 0; i < adjacency.Degree(u); i++)
                {
                    context.Checkpoint();
                    int v = adjacency.NeighborAt(u, i);
                    if (color[v] == -1) { color[v] = 1 - color[u]; queue[tail++] = v; }
                    else if (color[v] == color[u]) return (null, u, v);
                }
            }
        }
        return (color, -1, -1);
    }
}