using Engine.Domain.Operations;
using static Engine.Domain.Operations.FillMatrixOperation;
using static Engine.Domain.Operations.GenerateMazeOperation;
using static Engine.Domain.Operations.MatrixToGraphOperation;
using static Engine.Domain.Operations.PlaceItemsOperation;
using static Engine.Domain.Operations.SettleDebtsOperation;
using static Engine.Gateway.StepVisuals;

namespace Engine.Gateway;

/// <summary>
/// What every operation returns and which steps it emits, declared against the real C# result and payload
/// types (field names and kinds are read from them by reflection). OperationOutputTests runs every operation
/// and fails if a value or step it produces is not declared here, so this table cannot silently go stale.
/// </summary>
public static class OperationOutputs
{
    private static StepDescriptor Step<TPayload>(string type, string description, VisualHint visual) =>
        new(type, description, visual, ShapeReader.FieldsOf(typeof(TPayload))) { PayloadType = typeof(TPayload) };

    private static StepDescriptor Step(string type, string description) =>
        new(type, description, Nothing, []);

    private static ReturnsDescriptor Returns<T>(string description, bool canBeNull = false) =>
        new(description, ShapeReader.Of(typeof(T), canBeNull));

    private static OperationOutput Out(ReturnsDescriptor? returns, params StepDescriptor[] steps) => new(returns, steps);
    private static OperationOutput Steps(params StepDescriptor[] steps) => new(null, steps);

    public static IReadOnlyDictionary<string, OperationOutput> ById { get; } = new Dictionary<string, OperationOutput>
    {
        // ---------- matrix ----------
        { "set-cell", Out(Returns<int>("The value the cell held before."),
            Step<CellChangedPayload>("CellChanged", "A cell was overwritten.", Cell("Row", "Column"))) },
        { "fill-region", Out(Returns<int>("How many cells changed value."),
            Step<CellChangedPayload>("CellChanged", "A cell in the region was overwritten.", Cell("Row", "Column"))) },
        { "clear-matrix", Out(Returns<int>("How many non-zero cells were cleared."),
            Step<MatrixClearedPayload>("MatrixCleared", "Every cell was set to zero.", Nothing)) },
        { "fill-even-odd", Steps(
            Step<CellFilledPayload>("CellFilled", "A cell received its pattern value.", Cell("Row", "Column"))) },
        { "flood-fill", Steps(
            Step<CellFloodedPayload>("CellFlooded", "A cell of the connected region was recoloured.", Cell("Row", "Column"))) },
        { "place-items", Out(Returns<IReadOnlyList<int>>("Ids of the items that did not fit."),
            Step<ItemPlacedPayload>("ItemPlaced", "An item was placed; row and column are its top-left cell.", Cell("Row", "Column")),
            Step<ItemUnplacedPayload>("ItemUnplaced", "An item found no free spot.", Nothing)) },
        { "random-fill", Steps(
            Step<CellFilledPayload>("CellFilled", "A cell received a random value.", Cell("Row", "Column"))) },
        { "generate-maze-grid", Steps(
            Step<CellOpenedPayload>("CellOpened", "A cell became a passage.", Cell("Row", "Column"))) },
        { "transpose", Steps(Step<MatrixReshapedPayload>("MatrixReshaped", "The grid changed shape.", Nothing)) },
        { "rotate", Steps(Step<MatrixReshapedPayload>("MatrixReshaped", "The grid changed shape.", Nothing)) },
        { "flip", Steps(Step<MatrixReshapedPayload>("MatrixReshaped", "The grid was mirrored.", Nothing)) },
        { "crop", Steps(Step<MatrixReshapedPayload>("MatrixReshaped", "The grid was cut down to the chosen window.", Nothing)) },
        { "aggregate", Out(Returns<AggregateResult>("Count, sum, minimum and maximum of the selected cells."),
            Step("Aggregated", "The selected cells were summarised.")) },
        { "find-value", Out(Returns<FindValueResult>("Every matching position (capped) and the total match count."),
            Step<ValueFoundPayload>("ValueFound", "A cell holds the searched value.", Cell("Row", "Column"))) },
        { "count-regions", Out(Returns<RegionsResult>("How many connected regions of equal value exist and how big the largest is."),
            Step<RegionFoundPayload>("RegionFound", "A new region was discovered at this cell.", Cell("Row", "Column"))) },
        { "read-cell", Out(Returns<int>("The value stored in the cell.")) },
        { "game-of-life-step", Out(Returns<LifeResult>("Generations run, living cells left and whether the pattern is stable."),
            Step<CellChangedPayload>("CellChanged", "A cell was born or died.", Cell("Row", "Column")),
            Step<GenerationCompletedPayload>("GenerationCompleted", "One generation finished.", Nothing)) },
        { "grid-shortest-path", Out(Returns<GridPathResult>("Whether a route exists, its length and (unless omitted) the cells along it."),
            Step("EndpointBlocked", "The start or target cell is a wall."),
            Step<CellVisitedPayload>("CellVisited", "The search reached a cell.", Cell("Row", "Column")),
            Step("NoPath", "No route exists between the two cells."),
            Step("PathFound", "A route was found.")) },
        { "matrix-to-graph", Out(Returns<IReadOnlyList<GridPosition>>("The grid cell behind each node, indexed by node number."),
            Step<NodeMappedPayload>("NodeMapped", "A cell became a graph node.", Cell("Row", "Column")),
            Step<MatrixEdgeAddedPayload>("EdgeAdded", "Two neighbouring cells were connected.", Edge("From", "To"))) },

        // ---------- graph ----------
        { "add-node", Out(Returns<NodesAddedResult>("Index of the first new node and how many were added."),
            Step<NodeAddedPayload>("NodeAdded", "An isolated node was appended.", Node("Node"))) },
        { "add-connection", Out(Returns<bool>("True if a connection was added, false if it already existed."),
            Step("ConnectionAlreadyExists", "The connection was already there; nothing changed."),
            Step<ConnectionAddedPayload>("ConnectionAdded", "A connection was added.", Edge("From", "To"))) },
        { "remove-connection", Out(Returns<bool>("True if a connection was removed."),
            Step<ConnectionRemovedPayload>("ConnectionRemoved", "A connection was removed.", Edge("From", "To")),
            Step("ConnectionNotFound", "There was no such connection.")) },
        { "set-edge-weight", Out(Returns<int>("How many stored edges matched and were changed."),
            Step<EdgeWeightChangedPayload>("EdgeWeightChanged", "An edge received a new weight.", Edge("From", "To")),
            Step("EdgeNotFound", "There was no such edge.")) },
        { "isolate-node", Out(Returns<int>("How many connections were stripped from the node."),
            Step<EdgeRemovedForIsolationPayload>("EdgeRemovedForIsolation", "A connection of the node was removed.", Edge("From", "To")),
            Step("NodeAlreadyIsolated", "The node had no connections.")) },
        { "remove-graph-node", Out(Returns<RemoveGraphNodeResult>("The removed node, edges lost, new node count and the old index of each remaining node."),
            Step<GraphNodeRemovedPayload>("GraphNodeRemoved", "The node and its connections were deleted.", Node("Node"))) },
        { "clear-edges", Out(Returns<ClearGraphResult>("How many edges and nodes were removed."),
            Step<EdgesClearedPayload>("EdgesCleared", "The graph was emptied.", Nothing)) },
        { "reverse-edges", Steps(Step<EdgesReversedPayload>("EdgesReversed", "Every edge changed direction.", Nothing)) },
        { "subgraph", Out(Returns<SubgraphResult>("The original index of each kept node and how many edges survived."),
            Step<NodeKeptPayload>("NodeKept", "A node was kept and renumbered.", Node("OriginalIndex"))) },
        { "traverse", Out(Returns<TraversalResult>("The visit order (capped), the true number of nodes reached and whether the list was cut."),
            Step<TraversalStepPayload>("TraversalStep", "A node was visited.", Node("Node"))) },
        { "degree-stats", Out(Returns<DegreeStatsResult>("Node and edge counts plus degree extremes."),
            Step("DegreesComputed", "Degrees were counted.")) },
        { "has-cycle", Out(Returns<CycleResult>("Whether a cycle exists and one edge that closes it."),
            Step("NoCycle", "No cycle exists."),
            Step<CycleFoundPayload>("CycleFound", "This edge closes a cycle.", Edge("From", "To"))) },
        { "reachability-count", Out(Returns<int>("How many nodes can be reached from the start, including the start."),
            Step("NodeVisited", "A node was reached.")) },
        { "find-connected-components", Out(Returns<IReadOnlyList<IReadOnlyList<int>>>("The nodes of each component."),
            Step<ComponentFoundPayload>("ComponentFound", "A component was completed.", Nodes("Nodes"))) },
        { "find-articulation-points", Out(Returns<IReadOnlyList<int>>("Nodes whose removal disconnects the graph."),
            Step<NodeVisitedPayload>("NodeVisited", "The depth-first search entered a node.", Node("Node")),
            Step<ArticulationPointFoundPayload>("ArticulationPointFound", "This node is an articulation point.", Node("Node"))) },
        { "find-bridges", Out(Returns<IReadOnlyList<BridgeEdge>>("Edges whose removal disconnects the graph."),
            Step<BridgeFoundPayload>("BridgeFound", "This edge is a bridge.", Edge("From", "To"))) },
        { "is-bipartite", Out(Returns<BipartiteResult>("Whether the graph is two-colourable, the colours (capped) or a conflicting edge."),
            Step<BipartiteConflictPayload>("NotBipartite", "This edge joins two nodes of the same colour.", Edge("From", "To")),
            Step<NodeColoredPayload>("NodeColored", "A node received a side.", Node("Node"))) },
        { "topological-sort", Out(Returns<TopologicalSortResult>("Whether an order exists and the order found (capped)."),
            Step<NodeOrderedPayload>("NodeOrdered", "A node was placed in the order.", Node("Node")),
            Step("CycleBlocksSort", "Remaining nodes lie on or behind a cycle.")) },
        { "strongly-connected-components", Out(Returns<SccResult>("Component count, the largest size and the components (capped)."),
            Step<SccFoundPayload>("ComponentFound", "A strongly connected component was completed; firstNode is one of its members.", Node("FirstNode"))) },
        { "graph-coloring", Out(Returns<ColoringResult>("Whether a colouring exists, how many colours it uses and each node's colour (capped)."),
            Step("NotColorable", "A node is joined to itself, so no colouring exists."),
            Step<NodeColoredPayload>("NodeColored", "A node received a colour.", Node("Node"))) },
        { "centrality", Out(Returns<CentralityResult>("The measure used and the highest-scoring nodes."),
            Step<SourceProcessedPayload>("SourceProcessed", "One source node was fully processed.", Node("Source"))) },
        { "shortest-path", Out(Returns<ShortestPathResult>("The path and its total distance; null when the target is unreachable.", canBeNull: true),
            Step<NodeSettledPayload>("NodeSettled", "A node's final distance was fixed.", Node("Node")),
            Step("NoPathFound", "The target cannot be reached."),
            Step<ShortestPathResult>("PathFound", "The path was reconstructed.", Route("Path"))) },
        { "bellman-ford", Out(Returns<BellmanFordResult>("Whether the target is reachable, the distance, the path and whether a negative cycle blocks the answer."),
            Step<RelaxationRoundPayload>("RelaxationRound", "One pass over all edges finished.", Nothing),
            Step("NegativeCycle", "A negative cycle is reachable."),
            Step("NoPath", "The target is not reachable."),
            Step("PathFound", "A shortest path was found.")) },
        { "all-pairs-shortest-paths", Out(Returns<AllPairsResult>("Reachable pair count, the diameter and (for small graphs) the distance table."),
            Step<PivotProcessedPayload>("PivotProcessed", "All routes through this node were considered.", Node("Pivot")),
            Step("NegativeCycle", "A negative cycle makes distances meaningless.")) },
        { "minimum-spanning-tree", Out(Returns<bool>("True if the result spans every node."),
            Step<EdgeConsideredPayload>("EdgeConsidered", "The next-cheapest edge is examined.", Edge("From", "To")),
            Step<EdgeAddedPayload>("EdgeAdded", "The edge joined two components and was kept.", Edge("From", "To")),
            Step<EdgeRejectedPayload>("EdgeRejected", "The edge would close a cycle and was skipped.", Edge("From", "To"))) },
        { "max-flow", Out(Returns<MaxFlowResult>("The flow value, per-edge flows and the minimum cut."),
            Step<FlowAugmentedPayload>("FlowAugmented", "An augmenting path added flow.", Nothing)) },
        { "bipartite-matching", Out(Returns<MatchingResult>("Whether the graph is bipartite, the matching size and the matched pairs."),
            Step("NotBipartite", "The graph is not bipartite."),
            Step<MatchedPair>("PairMatched", "Two nodes were matched.", Edge("Left", "Right"))) },
        { "settle-debts", Steps(
            Step<BalanceComputedPayload>("BalanceComputed", "A node's net balance was computed.", Node("Node")),
            Step<TransactionCreatedPayload>("TransactionCreated", "A payment settles part of the debts.", Edge("From", "To"))) },
        { "generate-random-graph", Steps(
            Step<ConnectionAddedPayload>("ConnectionAdded", "A random connection was added.", Edge("From", "To"))) },
        { "generate-maze", Steps(
            Step<PassageCarvedPayload>("PassageCarved", "A passage joins two cells of the maze.", Edge("From", "To"))) },
        { "graph-to-matrix", Steps(
            Step<AdjacencyCellPayload>("AdjacencyCellSet", "An adjacency-matrix cell was filled.", Cell("Row", "Column"))) },

        // ---------- tree ----------
        { "insert-node", Steps(
            Step<NodeComparedPayload>("NodeCompared", "The new value was compared with this node.", Slot("NodeIndex")),
            Step<NodeInsertedPayload>("NodeInserted", "The value was attached as a new leaf.", Slot("Index"))) },
        { "remove-node", Out(Returns<bool>("True if the value was in the tree and removed."),
            Step<NodeComparedPayload>("NodeCompared", "The value was compared with this node.", Slot("NodeIndex")),
            Step("NodeNotFound", "The value is not in the tree."),
            Step<NodeRemovedPayload>("NodeRemoved", "The node was removed.", Nothing),
            Step<SuccessorFoundPayload>("SuccessorFound", "The in-order successor replaces the removed value.", Slot("SuccessorIndex"))) },
        { "tree-clear", Out(Returns<int>("How many nodes were removed."),
            Step<TreeClearedPayload>("TreeCleared", "The tree was emptied.", Nothing)) },
        { "tree-rebuild-balanced", Out(Returns<TreeRebuildResult>("Node count and the height before and after."),
            Step<NodePlacedPayload>("NodePlaced", "A value was placed in the rebuilt tree.", Slot("Index"))) },
        { "tree-contains", Out(Returns<TreeSearchResult>("Whether the value exists, where, and how many comparisons it took."),
            Step<NodeComparedPayload>("NodeCompared", "The value was compared with this node.", Slot("NodeIndex")),
            Step("NotFound", "The search ran off the tree.")) },
        { "tree-traverse", Out(Returns<IReadOnlyList<int>>("The values in visit order."),
            Step<TreeNodeVisitedPayload>("TreeNodeVisited", "A node was visited.", Slot("NodeIndex"))) },
        { "tree-stats", Out(Returns<TreeStatsResult>("Count, smallest and largest value, height and leaf count."),
            Step("TreeEmpty", "The tree has no nodes."),
            Step<LevelScannedPayload>("LevelScanned", "One level was measured.", Nothing)) },
        { "tree-kth-smallest", Out(Returns<KthSmallestResult>("Whether the k-th value exists, the value and its position."),
            Step<TreeNodeVisitedPayload>("TreeNodeVisited", "A node was counted in sorted order.", Slot("NodeIndex")),
            Step("KOutOfRange", "The tree has fewer than k nodes.")) },
        { "tree-lca", Out(Returns<LcaResult>("Whether both values exist and their lowest common ancestor."),
            Step("ValueMissing", "One or both values are not in the tree."),
            Step<NodeComparedPayload>("NodeCompared", "The search passed this node.", Slot("NodeIndex"))) },
        { "generate-tree", Steps(
            Step<NodeInsertedPayload>("NodeInserted", "A random value was inserted.", Slot("Index"))) },
        { "tree-to-graph", Out(Returns<IReadOnlyList<TreeNodeMapping>>("The tree slot and value behind each graph node."),
            Step<TreeNodeMappedPayload>("TreeNodeMapped", "A tree node became a graph node.", Slot("TreeSlot"))) },
    };
}
