using Engine.Domain;
using Engine.Domain.Capabilities;

namespace Engine.Domain.Datasets;

/// <summary>
/// Compressed Sparse Row graph. Nodes are int indices 0..NodeCount-1.
/// Directed, optionally weighted — unweighted edges default to weight 1.0.
/// </summary>
public sealed class Graph : IDataset, ITraversable<int>, IWeighted<int>
{
    private readonly int[] _rowOffsets;
    private readonly int[] _edges;
    private readonly double[] _weights;
    public int EdgeCount => _edges.Length; // directed edges as stored
    public int NodeCount => _rowOffsets.Length - 1;
    public double WeightAt(int node, int index) => _weights[_rowOffsets[node] + index];
    public int Degree(int node) => _rowOffsets[node + 1] - _rowOffsets[node];
    public int NeighborAt(int node, int index) => _edges[_rowOffsets[node] + index];

    public void EnsureNode(int node, string parameterName)
    {
        if ((uint)node >= (uint)NodeCount)
            throw new ArgumentException(
                NodeCount == 0
                    ? $"{parameterName} {node} is invalid: the graph has no nodes"
                    : $"{parameterName} {node} is outside the valid range 0..{NodeCount - 1}",
                parameterName);
    }
    public Graph(int nodeCount, IEnumerable<(int From, int To, double Weight)> edges)
    {
        if (nodeCount < 0)
            throw new ArgumentException($"nodeCount must be non-negative (got {nodeCount})");
        DatasetLimits.EnsureWithinLimit(nodeCount, $"Graph with {nodeCount} nodes");
        var buckets = new List<(int To, double Weight)>[nodeCount];
        for (int i = 0; i < nodeCount; i++) buckets[i] = [];

        int edgeCount = 0;
        foreach (var (from, to, weight) in edges)
        {
            if (++edgeCount > DatasetLimits.MaxEdges)
                throw new ArgumentException($"Graph would contain more than {DatasetLimits.MaxEdges:N0} directed edges");
            if ((uint)from >= (uint)nodeCount || (uint)to >= (uint)nodeCount)
                throw new ArgumentException(
                    $"Edge ({from} -> {to}) references a node outside the valid range 0..{nodeCount - 1}");
            buckets[from].Add((to, weight));
        }

        _rowOffsets = new int[nodeCount + 1];
        var flatEdges = new List<int>();
        var flatWeights = new List<double>();
        for (int i = 0; i < nodeCount; i++)
        {
            _rowOffsets[i] = flatEdges.Count;
            foreach (var (to, weight) in buckets[i])
            {
                flatEdges.Add(to);
                flatWeights.Add(weight);
            }
        }
        _rowOffsets[nodeCount] = flatEdges.Count;
        _edges = [.. flatEdges];
        _weights = [.. flatWeights];
    }

    // Unweighted convenience overload — existing callers (maze generation) use this unchanged.
    public Graph(int nodeCount, IEnumerable<(int From, int To)> edges)
        : this(nodeCount, edges.Select(e => (e.From, e.To, 1.0))) { }

    public static Graph Empty(int nodeCount) => new(nodeCount, Enumerable.Empty<(int, int, double)>());

    public IEnumerable<int> GetNeighbors(int node)
    {
        int start = _rowOffsets[node];
        int end = _rowOffsets[node + 1];
        for (int i = start; i < end; i++)
            yield return _edges[i];
    }

    public IEnumerable<(int Neighbor, double Weight)> GetWeightedNeighbors(int node)
    {
        int start = _rowOffsets[node];
        int end = _rowOffsets[node + 1];
        for (int i = start; i < end; i++)
            yield return (_edges[i], _weights[i]);
    }

    public double GetWeight(int from, int to)
    {
        foreach (var (neighbor, weight) in GetWeightedNeighbors(from))
            if (neighbor == to) return weight;
        throw new ArgumentException($"No edge from {from} to {to}");
    }
    public IEnumerable<(int From, int To, double Weight)> AllDirectedEdges()
    {
        for (int node = 0; node < NodeCount; node++)
            foreach (var (neighbor, weight) in GetWeightedNeighbors(node))
                yield return (node, neighbor, weight);
    }
}