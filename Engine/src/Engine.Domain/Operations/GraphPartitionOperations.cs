using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record NodeColoredPayload(int Node, int Color);
public sealed record BipartiteConflictPayload(int From, int To);
public sealed record BipartiteResult(
    bool IsBipartite, IReadOnlyList<int>? Colors, bool ColorsTruncated, int? ConflictFrom, int? ConflictTo);

public sealed record NodeOrderedPayload(int Node, int Position);
public sealed record TopologicalSortResult(bool IsAcyclic, IReadOnlyList<int> Order, int SortedCount, bool Truncated);

public sealed record SccFoundPayload(int Index, int Size, int FirstNode);
public sealed record SccResult(
    int ComponentCount, int LargestComponentSize, IReadOnlyList<IReadOnlyList<int>> Components, bool Truncated);

public sealed record ColoringResult(bool Colorable, int ColorCount, IReadOnlyList<int> Colors, bool Truncated);

/// <summary>
/// Two-colours the graph so no connection joins two nodes of the same colour, or reports an edge that
/// makes it impossible (an odd cycle, or a self-loop). Direction is ignored. Isolated nodes get colour 0.
/// Colors lists the first DatasetLimits.MaxReturnedItems nodes.
/// </summary>
public sealed class IsBipartiteOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("is-bipartite", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount);
        var (colors, conflictFrom, conflictTo) = BipartiteColoring.Compute(new UndirectedAdjacency(input), context);
        var trace = new TraceRecorder(context);

        if (colors is null)
        {
            trace.Add(new DomainStepEvent("NotBipartite", $"Nodes {conflictFrom} and {conflictTo} are joined but would share a colour",
                new BipartiteConflictPayload(conflictFrom, conflictTo)));
            return new OperationExecutionResult(true, null,
                new BipartiteResult(false, null, false, conflictFrom, conflictTo), trace, null);
        }

        for (int node = 0; node < colors.Length; node++)
            trace.Add(new DomainStepEvent("NodeColored", $"Node {node} -> colour {colors[node]}",
                new NodeColoredPayload(node, colors[node])));

        int listed = Math.Min(colors.Length, DatasetLimits.MaxReturnedItems);
        var result = new BipartiteResult(true, colors.Take(listed).ToArray(), colors.Length > listed, null, null);
        return new OperationExecutionResult(true, null, result, trace, null);
    }
}

/// <summary>
/// Kahn's algorithm, always taking the smallest available node, so the order is the lexicographically
/// smallest valid one and fully deterministic. Edges are followed as stored. If a cycle blocks it,
/// IsAcyclic is false and Order holds the prefix that could be ordered (SortedCount of NodeCount).
/// </summary>
public sealed class TopologicalSortOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("topological-sort", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        context.Checkpoint(n + (long)input.EdgeCount);

        var inDegree = new int[n];
        foreach (var (_, to, _) in input.AllDirectedEdges()) inDegree[to]++;

        var ready = new PriorityQueue<int, int>();
        for (int node = 0; node < n; node++)
            if (inDegree[node] == 0) ready.Enqueue(node, node);

        var trace = new TraceRecorder(context);
        var order = new List<int>();
        int sorted = 0;

        while (ready.TryDequeue(out int u, out _))
        {
            sorted++;
            if (order.Count < DatasetLimits.MaxReturnedItems) order.Add(u);
            trace.Add(new DomainStepEvent("NodeOrdered", $"#{sorted}: node {u}", new NodeOrderedPayload(u, sorted)));

            for (int i = 0; i < input.Degree(u); i++)
            {
                int v = input.NeighborAt(u, i);
                if (--inDegree[v] == 0) ready.Enqueue(v, v);
            }
        }

        bool acyclic = sorted == n;
        if (!acyclic)
            trace.Add(new DomainStepEvent("CycleBlocksSort", $"{n - sorted} node(s) lie on a cycle or behind one and cannot be ordered"));

        return new OperationExecutionResult(true, null,
            new TopologicalSortResult(acyclic, order, sorted, sorted > order.Count), trace, null);
    }
}

/// <summary>
/// Strongly connected components (iterative Tarjan), following edges as stored. Components are listed in
/// the order Tarjan finishes them, which is sink-first (reverse topological order of the condensation);
/// nodes within each component are ascending. Listing stops once DatasetLimits.MaxReturnedItems nodes
/// have been listed (Truncated); ComponentCount and LargestComponentSize are always exact.
/// </summary>
public sealed class StronglyConnectedComponentsOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("strongly-connected-components", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        context.Checkpoint(n + (long)input.EdgeCount);

        var index = new int[n];
        Array.Fill(index, -1);
        var low = new int[n];
        var onStack = new bool[n];
        var cursor = new int[n];
        var componentStack = new Stack<int>();
        var callStack = new Stack<int>();
        var components = new List<IReadOnlyList<int>>();
        var trace = new TraceRecorder(context);

        int nextIndex = 0, componentCount = 0, largest = 0, listedNodes = 0;
        bool truncated = false;

        for (int root = 0; root < n; root++)
        {
            if (index[root] != -1) continue;
            index[root] = low[root] = nextIndex++;
            componentStack.Push(root);
            onStack[root] = true;
            callStack.Push(root);

            while (callStack.Count > 0)
            {
                int u = callStack.Peek();
                if (cursor[u] < input.Degree(u))
                {
                    int v = input.NeighborAt(u, cursor[u]++);
                    if (index[v] == -1)
                    {
                        index[v] = low[v] = nextIndex++;
                        componentStack.Push(v);
                        onStack[v] = true;
                        callStack.Push(v);
                    }
                    else if (onStack[v])
                    {
                        low[u] = Math.Min(low[u], index[v]);
                    }
                    continue;
                }

                callStack.Pop();
                if (callStack.Count > 0)
                {
                    int parent = callStack.Peek();
                    low[parent] = Math.Min(low[parent], low[u]);
                }
                if (low[u] != index[u]) continue;

                var component = new List<int>();
                int member;
                do
                {
                    member = componentStack.Pop();
                    onStack[member] = false;
                    component.Add(member);
                } while (member != u);

                component.Sort();
                componentCount++;
                largest = Math.Max(largest, component.Count);
                trace.Add(new DomainStepEvent("ComponentFound", $"Component {componentCount}: {component.Count} node(s)",
                    new SccFoundPayload(componentCount, component.Count, component[0])));

                if (!truncated && listedNodes + component.Count <= DatasetLimits.MaxReturnedItems)
                {
                    components.Add(component);
                    listedNodes += component.Count;
                }
                else
                {
                    truncated = true;
                }
            }
        }

        return new OperationExecutionResult(true, null, new SccResult(componentCount, largest, components, truncated), trace, null);
    }
}

/// <summary>
/// Greedy (Welsh-Powell) colouring on the undirected structure: nodes in descending degree order, each
/// taking the smallest colour none of its neighbours has. A heuristic, not a minimum: it never needs more
/// than maxDegree + 1 colours. A self-loop makes a proper colouring impossible (Colorable = false).
/// </summary>
public sealed class GraphColoringOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("graph-coloring", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        context.Checkpoint(n + (long)input.EdgeCount);
        var adjacency = new UndirectedAdjacency(input);
        var trace = new TraceRecorder(context);

        int maxDegree = 0;
        for (int node = 0; node < n; node++)
        {
            maxDegree = Math.Max(maxDegree, adjacency.Degree(node));
            for (int i = 0; i < adjacency.Degree(node); i++)
            {
                if (adjacency.NeighborAt(node, i) != node) continue;
                trace.Add(new DomainStepEvent("NotColorable", $"Node {node} is joined to itself"));
                return new OperationExecutionResult(true, null, new ColoringResult(false, 0, [], false), trace, null);
            }
        }

        int[] order = Enumerable.Range(0, n)
            .OrderByDescending(node => adjacency.Degree(node)).ThenBy(node => node).ToArray();

        var colors = new int[n];
        Array.Fill(colors, -1);
        var stamp = new int[maxDegree + 2]; // stamp[c] == this node's turn means a neighbour already uses colour c
        int colorCount = 0, turn = 0;

        foreach (int node in order)
        {
            turn++;
            for (int i = 0; i < adjacency.Degree(node); i++)
            {
                int neighborColor = colors[adjacency.NeighborAt(node, i)];
                if (neighborColor >= 0) stamp[neighborColor] = turn;
            }

            int color = 0;
            while (stamp[color] == turn) color++;
            colors[node] = color;
            colorCount = Math.Max(colorCount, color + 1);
            trace.Add(new DomainStepEvent("NodeColored", $"Node {node} -> colour {color}", new NodeColoredPayload(node, color)));
        }

        int listed = Math.Min(n, DatasetLimits.MaxReturnedItems);
        var result = new ColoringResult(true, colorCount, colors.Take(listed).ToArray(), n > listed);
        return new OperationExecutionResult(true, null, result, trace, null);
    }
}