using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public enum GraphTraversalOrder { Bfs, Dfs }

public sealed record TraversalStepPayload(int Node, int? Parent, int Position, int Depth);
public sealed record TraversalResult(string Order, IReadOnlyList<int> Nodes, int TotalVisited, bool Truncated);
public sealed record DegreeStatsResult(
    int NodeCount, int EdgeCount, int IsolatedNodes, int SelfLoops,
    int MinOutDegree, int MaxOutDegree, int? MaxOutDegreeNode,
    int MaxInDegree, int? MaxInDegreeNode, double AverageOutDegree);
public sealed record CycleFoundPayload(int From, int To);
public sealed record CycleResult(bool HasCycle, int? From, int? To);

/// <summary>
/// BFS or DFS from `start`, following edges as stored (so on a directed graph only reachable nodes are
/// visited). DFS is the true recursive pre-order, produced iteratively so depth can't overflow the stack.
/// Nodes lists the first DatasetLimits.MaxReturnedItems in visit order; TotalVisited is always exact.
/// </summary>
public sealed class TraverseGraphOperation : IOperation<Graph>
{
    private readonly int _start;
    private readonly GraphTraversalOrder _order;

    public TraverseGraphOperation(int start, string order = "bfs")
    {
        ArgumentNullException.ThrowIfNull(order);
        _start = start;
        _order = order.ToLowerInvariant() switch
        {
            "bfs" => GraphTraversalOrder.Bfs,
            "dfs" => GraphTraversalOrder.Dfs,
            _ => throw new ArgumentException($"Unknown traversal order '{order}'; expected bfs or dfs")
        };
    }

    public OperationMetadata Metadata { get; } = new("traverse", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        input.EnsureNode(_start, "start");
        int n = input.NodeCount;
        context.Checkpoint(n);

        var trace = new TraceRecorder(context);
        var visited = new bool[n];
        var depth = new int[n];
        var nodes = new List<int>();
        int total = 0;

        void Visit(int node, int? parent)
        {
            visited[node] = true;
            depth[node] = parent is int p ? depth[p] + 1 : 0;
            total++;
            if (nodes.Count < DatasetLimits.MaxReturnedItems) nodes.Add(node);
            trace.Add(new DomainStepEvent("TraversalStep",
                parent is null ? $"Started at {node}" : $"Visited {node} from {parent}",
                new TraversalStepPayload(node, parent, total, depth[node])));
        }

        if (_order == GraphTraversalOrder.Bfs)
        {
            var queue = new Queue<int>();
            Visit(_start, null);
            queue.Enqueue(_start);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                for (int i = 0; i < input.Degree(u); i++)
                {
                    int v = input.NeighborAt(u, i);
                    if (visited[v]) continue;
                    Visit(v, u);
                    queue.Enqueue(v);
                }
            }
        }
        else
        {
            var cursor = new int[n];
            var stack = new Stack<int>();
            Visit(_start, null);
            stack.Push(_start);
            while (stack.Count > 0)
            {
                int u = stack.Peek();
                if (cursor[u] < input.Degree(u))
                {
                    int v = input.NeighborAt(u, cursor[u]++);
                    if (visited[v]) continue;
                    Visit(v, u);
                    stack.Push(v);
                }
                else
                {
                    stack.Pop();
                }
            }
        }

        var result = new TraversalResult(_order.ToString().ToLowerInvariant(), nodes, total, total > nodes.Count);
        return new OperationExecutionResult(true, null, result, trace, null);
    }
}

/// <summary>
/// Degree statistics over the edges as stored: an undirected connection is two directed edges, so it
/// counts once toward each endpoint's out-degree and in-degree. Ties go to the lowest node index.
/// </summary>
public sealed class DegreeStatsOperation : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("degree-stats", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        context.Checkpoint(n + (long)input.EdgeCount);

        var inDegree = new int[n];
        int selfLoops = 0;
        foreach (var (from, to, _) in input.AllDirectedEdges())
        {
            inDegree[to]++;
            if (from == to) selfLoops++;
        }

        int isolated = 0, minOut = n == 0 ? 0 : int.MaxValue;
        int maxOut = 0, maxIn = 0;
        int? maxOutNode = null, maxInNode = null;

        for (int node = 0; node < n; node++)
        {
            int outDegree = input.Degree(node);
            int inward = inDegree[node];
            if (outDegree == 0 && inward == 0) isolated++;
            if (outDegree < minOut) minOut = outDegree;
            if (maxOutNode is null || outDegree > maxOut) { maxOut = outDegree; maxOutNode = node; }
            if (maxInNode is null || inward > maxIn) { maxIn = inward; maxInNode = node; }
        }

        var stats = new DegreeStatsResult(n, input.EdgeCount, isolated, selfLoops, minOut, maxOut, maxOutNode,
            maxIn, maxInNode, n == 0 ? 0 : (double)input.EdgeCount / n);

        var trace = new TraceRecorder(context);
        trace.Add(new DomainStepEvent("DegreesComputed",
            $"{n} node(s), {input.EdgeCount} edge(s), {isolated} isolated, {selfLoops} self-loop(s)"));
        return new OperationExecutionResult(true, null, stats, trace, null);
    }
}

/// <summary>
/// Cycle detection. Value reports one edge that closes a cycle (not the whole path).
///
/// directed = false (default): every stored edge is an undirected connection, matching bridges /
///   articulation points / MST. A connection stored in both directions counts once; a duplicated
///   connection or a self-loop is a cycle. Works for any storage, symmetric or not.
/// directed = true: follows edges as stored, so a symmetric pair (0->1, 1->0) IS a 2-cycle here.
///
/// Both modes are iterative: graph shape cannot overflow the stack.
/// </summary>
public sealed class HasCycleOperation(bool directed = false) : IOperation<Graph>
{
    public OperationMetadata Metadata { get; } = new("has-cycle", StateEffect.ProducesValue);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        context.Checkpoint(input.NodeCount + (long)input.EdgeCount);

        var closing = directed ? FindDirectedCycle(input) : FindUndirectedCycle(input, context);

        var trace = new TraceRecorder(context);
        if (closing is null)
        {
            trace.Add(new DomainStepEvent("NoCycle", directed ? "The graph is acyclic" : "The graph has no cycles"));
            return new OperationExecutionResult(true, null, new CycleResult(false, null, null), trace, null);
        }

        var edge = closing.Value;
        trace.Add(new DomainStepEvent("CycleFound", $"Edge {edge.From} -> {edge.To} closes a cycle",
            new CycleFoundPayload(edge.From, edge.To)));
        return new OperationExecutionResult(true, null, new CycleResult(true, edge.From, edge.To), trace, null);
    }

    // Three-colour DFS: an edge into a node that is still on the stack closes a cycle (u == v is a self-loop).
    private static (int From, int To)? FindDirectedCycle(Graph graph)
    {
        int n = graph.NodeCount;
        var state = new byte[n]; // 0 = unseen, 1 = on the stack, 2 = finished
        var cursor = new int[n];
        var stack = new Stack<int>();

        for (int root = 0; root < n; root++)
        {
            if (state[root] != 0) continue;
            state[root] = 1;
            stack.Push(root);

            while (stack.Count > 0)
            {
                int u = stack.Peek();
                if (cursor[u] < graph.Degree(u))
                {
                    int v = graph.NeighborAt(u, cursor[u]++);
                    if (state[v] == 0) { state[v] = 1; stack.Push(v); }
                    else if (state[v] == 1) return (u, v);
                }
                else
                {
                    state[u] = 2;
                    stack.Pop();
                }
            }
        }
        return null;
    }

    // Sort every edge as (from * n + to). Adjacent equal keys are duplicated connections, a == b is a
    // self-loop, and otherwise union-find over each connection once (a high->low edge is skipped when
    // its low->high twin exists, since that is the same connection stored in both directions).
    private static (int From, int To)? FindUndirectedCycle(Graph graph, OperationContext context)
    {
        int n = graph.NodeCount;
        var keys = new long[graph.EdgeCount];
        int k = 0;
        foreach (var (from, to, _) in graph.AllDirectedEdges())
            keys[k++] = (long)from * n + to;

        Array.Sort(keys);
        context.Checkpoint(keys.Length); // the sort is the expensive part

        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        for (int i = 0; i < keys.Length; i++)
        {
            int a = (int)(keys[i] / n), b = (int)(keys[i] % n);
            if (a == b || (i > 0 && keys[i] == keys[i - 1])) return (a, b);
            if (a > b && Array.BinarySearch(keys, (long)b * n + a) >= 0) continue;

            int ra = Find(a), rb = Find(b);
            if (ra == rb) return (a, b);
            parent[ra] = rb;
        }
        return null;
    }
}