using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Session;

namespace Engine.Gateway;

/// <summary>
/// Every operation this backend knows how to execute, grouped by the
/// dataset type it targets. Compile-time, explicit registration — chosen
/// over reflection/attribute-based auto-discovery because the full list
/// stays visible and navigable (Ctrl+click any registration to see exactly
/// what runs), at the cost of this file needing a one-line addition per
/// new operation. Revisit if the list grows large enough that manual
/// upkeep becomes the actual bottleneck — not the case yet.
/// </summary>
public static class OperationCatalog
{
    public static OperationRegistry BuildDefault()
    {
        var registry = new OperationRegistry();
        RegisterMatrixOperations(registry);
        RegisterGraphOperations(registry);
        RegisterTreeOperations(registry);
        return registry;
    }

    private static void RegisterMatrixOperations(OperationRegistry registry)
    {
        registry.Register(new OperationHandler<Matrix>("set-cell",
    options => new SetCellOperation(
        options["row"].GetInt32(), options["column"].GetInt32(), options["value"].GetInt32())));

        registry.Register(new OperationHandler<Matrix>("fill-region",
            options => new FillRegionOperation(
                options["row"].GetInt32(), options["column"].GetInt32(),
                options["height"].GetInt32(), options["width"].GetInt32(), options["value"].GetInt32())));

        registry.Register(new OperationHandler<Matrix>("clear-matrix", _ => new ClearMatrixOperation()));
        registry.Register(new OperationHandler<Matrix>("transpose", _ => new TransposeMatrixOperation()));

        registry.Register(new OperationHandler<Matrix>("rotate",
            options => new RotateMatrixOperation(
                options.TryGetValue("quarterTurns", out var turns) ? turns.GetInt32() : 1)));

        registry.Register(new OperationHandler<Matrix>("flip",
            options => new FlipMatrixOperation(
                options.TryGetValue("axis", out var axis) ? axis.GetString() ?? "horizontal" : "horizontal")));

        registry.Register(new OperationHandler<Matrix>("crop",
            options => new CropMatrixOperation(
                options["row"].GetInt32(), options["column"].GetInt32(),
                options["height"].GetInt32(), options["width"].GetInt32())));

        registry.Register(new OperationHandler<Matrix>("aggregate",
            options => new AggregateMatrixOperation(
                options.TryGetValue("scope", out var scope) ? scope.GetString() ?? "all" : "all",
                options.TryGetValue("index", out var index) ? index.GetInt32() : 0)));

        registry.Register(new OperationHandler<Matrix>("find-value",
            options => new FindValueOperation(
                options["value"].GetInt32(),
                options.TryGetValue("limit", out var limit) ? limit.GetInt32() : 1_000)));

        registry.Register(new OperationHandler<Matrix>("count-regions",
            options => new CountRegionsOperation(
                options.TryGetValue("diagonals", out var diagonals) && diagonals.GetBoolean(),
                options.TryGetValue("value", out var only) ? (int?)only.GetInt32() : null)));

        registry.Register(new OperationHandler<Matrix>("game-of-life-step",
            options => new GameOfLifeOperation(
                options.TryGetValue("generations", out var generations) ? generations.GetInt32() : 1,
                options.TryGetValue("wrap", out var wrap) && wrap.GetBoolean())));

        registry.Register(new OperationHandler<Matrix>("grid-shortest-path",
            options => new GridShortestPathOperation(
                options["startRow"].GetInt32(), options["startColumn"].GetInt32(),
                options["targetRow"].GetInt32(), options["targetColumn"].GetInt32(),
                options.TryGetValue("diagonals", out var moveDiagonally) && moveDiagonally.GetBoolean())));

        registry.Register(new OperationHandler<Matrix>("generate-maze-grid",
            options => new GenerateMazeGridOperation(
                options["width"].GetInt32(), options["height"].GetInt32(), options["seed"].GetInt32())));

        registry.Register(new OperationHandler<Matrix>("random-fill",
            options => new RandomFillOperation(
                options["seed"].GetInt32(),
                options.TryGetValue("maxValue", out var maxValue) ? maxValue.GetInt32() : 2)));
        registry.Register(new OperationHandler<Matrix>("fill-even-odd", _ => new FillMatrixOperation()));

        registry.Register(new OperationHandler<Matrix>("read-cell",
            options => new ReadCellQuery(options["row"].GetInt32(), options["column"].GetInt32())));

        registry.Register(new OperationHandler<Matrix>("flood-fill",
            options => new FloodFillOperation(
                options["row"].GetInt32(), options["column"].GetInt32(), options["newValue"].GetInt32())));

        registry.Register(new OperationHandler<Matrix>("place-items", options =>
        {
            var items = options["items"].EnumerateArray()
                .Select(e => new BinItem(
                    e.GetProperty("id").GetInt32(),
                    e.GetProperty("width").GetInt32(),
                    e.GetProperty("height").GetInt32()))
                .ToList();
            return new PlaceItemsOperation(items);
        }));

        registry.Register(new OperationHandler<Matrix>("matrix-to-graph", _ => new MatrixToGraphOperation()));
    }

    private static void RegisterGraphOperations(OperationRegistry registry)
    {
        registry.Register(new OperationHandler<Graph>("is-bipartite", _ => new IsBipartiteOperation()));
        registry.Register(new OperationHandler<Graph>("topological-sort", _ => new TopologicalSortOperation()));
        registry.Register(new OperationHandler<Graph>("strongly-connected-components",
            _ => new StronglyConnectedComponentsOperation()));

        registry.Register(new OperationHandler<Graph>("bellman-ford",
            options => new BellmanFordOperation(options["start"].GetInt32(), options["target"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("all-pairs-shortest-paths", _ => new AllPairsShortestPathsOperation()));

        registry.Register(new OperationHandler<Graph>("max-flow",
            options => new MaxFlowOperation(options["source"].GetInt32(), options["sink"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("bipartite-matching", _ => new BipartiteMatchingOperation()));

        registry.Register(new OperationHandler<Graph>("centrality",
            options => new CentralityOperation(
                options.TryGetValue("measure", out var measure) ? measure.GetString() ?? "betweenness" : "betweenness",
                options.TryGetValue("k", out var k) ? k.GetInt32() : 10,
                options.TryGetValue("directed", out var directedScores) && directedScores.GetBoolean())));

        registry.Register(new OperationHandler<Graph>("graph-coloring", _ => new GraphColoringOperation()));

        registry.Register(new OperationHandler<Graph>("generate-random-graph",
            options => new GenerateRandomGraphOperation(
                options["nodes"].GetInt32(), options["connections"].GetInt32(), options["seed"].GetInt32(),
                options.TryGetValue("directed", out var directedGraph) && directedGraph.GetBoolean(),
                options.TryGetValue("maxWeight", out var maxWeight) ? maxWeight.GetInt32() : 1)));
        registry.Register(new OperationHandler<Graph>("add-node",
            options => new AddNodeOperation(options.TryGetValue("count", out var count) ? count.GetInt32() : 1)));

        registry.Register(new OperationHandler<Graph>("set-edge-weight",
            options => new SetEdgeWeightOperation(
                options["from"].GetInt32(), options["to"].GetInt32(), options["weight"].GetDouble(),
                options.TryGetValue("symmetric", out var symmetric) && symmetric.GetBoolean())));

        registry.Register(new OperationHandler<Graph>("clear-edges",
            options => new ClearEdgesOperation(
                options.TryGetValue("removeNodes", out var removeNodes) && removeNodes.GetBoolean())));

        registry.Register(new OperationHandler<Graph>("reverse-edges", _ => new ReverseEdgesOperation()));

        registry.Register(new OperationHandler<Graph>("subgraph",
            options => new SubgraphOperation(options["nodes"].EnumerateArray().Select(e => e.GetInt32()).ToList())));

        registry.Register(new OperationHandler<Graph>("traverse",
            options => new TraverseGraphOperation(
                options["start"].GetInt32(),
                options.TryGetValue("order", out var order) ? order.GetString() ?? "bfs" : "bfs")));

        registry.Register(new OperationHandler<Graph>("degree-stats", _ => new DegreeStatsOperation()));

        registry.Register(new OperationHandler<Graph>("has-cycle",
            options => new HasCycleOperation(
                options.TryGetValue("directed", out var directed) && directed.GetBoolean())));

        registry.Register(new OperationHandler<Graph>("graph-to-matrix", _ => new GraphToMatrixOperation()));
        registry.Register(new OperationHandler<Graph>("generate-maze",
            options => new GenerateMazeOperation(
                options["width"].GetInt32(), options["height"].GetInt32(), options["seed"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("reachability-count",
            options => new ReachabilityQuery(options["startNode"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("shortest-path",
            options => new ShortestPathOperation(options["start"].GetInt32(), options["target"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("minimum-spanning-tree",
            _ => new MinimumSpanningTreeOperation()));

        registry.Register(new OperationHandler<Graph>("settle-debts", _ => new SettleDebtsOperation()));

        registry.Register(new OperationHandler<Graph>("add-connection",
            options => new AddConnectionOperation(
                options["from"].GetInt32(), options["to"].GetInt32(),
                options.TryGetValue("weight", out var w) ? w.GetDouble() : 1.0)));

        registry.Register(new OperationHandler<Graph>("remove-connection",
            options => new RemoveConnectionOperation(options["from"].GetInt32(), options["to"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("isolate-node",
            options => new IsolateNodeOperation(options["nodeIndex"].GetInt32())));

        registry.Register(new OperationHandler<Graph>("find-articulation-points",
            _ => new FindArticulationPointsOperation()));

        registry.Register(new OperationHandler<Graph>("find-bridges", _ => new FindBridgesOperation()));

        registry.Register(new OperationHandler<Graph>("find-connected-components",
            _ => new FindConnectedComponentsOperation()));
    }

    private static void RegisterTreeOperations(OperationRegistry registry)
    {
        registry.Register(new OperationHandler<Tree>("insert-node",
            options => new InsertNodeOperation(options["value"].GetInt32())));

        registry.Register(new OperationHandler<Tree>("remove-node",
            options => new RemoveNodeOperation(options["value"].GetInt32())));

        registry.Register(new OperationHandler<Tree>("tree-contains",
            options => new TreeContainsOperation(options["value"].GetInt32())));

        registry.Register(new OperationHandler<Tree>("tree-traverse",
            options => new TraverseTreeOperation(
                options.TryGetValue("order", out var order) ? order.GetString() ?? "in" : "in")));

        registry.Register(new OperationHandler<Tree>("tree-stats", _ => new TreeStatsOperation()));

        registry.Register(new OperationHandler<Tree>("tree-kth-smallest",
            options => new TreeKthSmallestOperation(options["k"].GetInt32())));

        registry.Register(new OperationHandler<Tree>("tree-lca",
            options => new TreeLowestCommonAncestorOperation(options["a"].GetInt32(), options["b"].GetInt32())));

        registry.Register(new OperationHandler<Tree>("tree-clear", _ => new ClearTreeOperation()));
        registry.Register(new OperationHandler<Tree>("tree-rebuild-balanced", _ => new RebuildBalancedTreeOperation()));
        registry.Register(new OperationHandler<Tree>("tree-to-graph", _ => new TreeToGraphOperation()));

        registry.Register(new OperationHandler<Tree>("generate-tree",
            options => new GenerateTreeOperation(
                options["count"].GetInt32(),
                options["seed"].GetInt32(),
                options.TryGetValue("maxValue", out var maxValue) ? maxValue.GetInt32() : 1_000_000)));
    }
}