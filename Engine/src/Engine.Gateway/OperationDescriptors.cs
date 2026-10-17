namespace Engine.Gateway;

/// <summary>
/// What a client needs to build a form for an operation without hard-coding it: a label, a category
/// to group by, a description, and each option's semantic kind. The "kind" says what the value MEANS
/// (a node index, a grid row, a random seed), not just its JSON type, so a UI can offer node pickers
/// or cell pickers instead of bare number boxes.
/// </summary>
public sealed record OptionDescriptor(
    string Name,
    string Kind,
    string Label,
    bool Required,
    object? Default = null,
    IReadOnlyList<string>? Choices = null);

public sealed record OperationDescriptor(
    string Id,
    string Label,
    string Category,
    string Description,
    IReadOnlyList<OptionDescriptor> Options);

public static class OptionKinds
{
    public const string Int = "int";
    public const string Number = "number";
    public const string Bool = "bool";
    public const string Choice = "choice";
    public const string Row = "row";
    public const string Column = "column";
    public const string Node = "node";
    public const string NodeList = "nodeList";
    public const string Seed = "seed";
    public const string ItemList = "itemList";
}

/// <summary>
/// Hand-written metadata for every registered operation. OperationDescriptorTests checks it against
/// the real handlers (same ids, same option names as OperationExamples, required flags that match what
/// the handler really demands), so it cannot drift silently.
/// </summary>
public static class OperationDescriptors
{
    private static readonly Dictionary<string, OperationDescriptor> ById = Build().ToDictionary(d => d.Id);

    public static IEnumerable<OperationDescriptor> All => ById.Values;

    public static OperationDescriptor? TryGet(string operationId) => ById.GetValueOrDefault(operationId);

    // ---------- option shorthands ----------

    private static OptionDescriptor Req(string name, string kind, string label) => new(name, kind, label, true);
    private static OptionDescriptor Opt(string name, string kind, string label, object? def) => new(name, kind, label, false, def);
    private static OptionDescriptor Pick(string name, string label, string def, params string[] choices) =>
        new(name, OptionKinds.Choice, label, false, def, choices);

    private static OperationDescriptor Op(string id, string label, string category, string description,
        params OptionDescriptor[] options) => new(id, label, category, description, options);

    private static IEnumerable<OperationDescriptor> Build()
    {
        // ---------- matrix ----------
        const string Edit = "Edit", Reshape = "Reshape", Query = "Query", Generate = "Generate", Convert = "Convert";

        yield return Op("set-cell", "Set cell", Edit, "Write one value into one cell.",
            Req("row", OptionKinds.Row, "Row"), Req("column", OptionKinds.Column, "Column"), Req("value", OptionKinds.Int, "Value"));
        yield return Op("fill-region", "Fill region", Edit, "Fill a rectangle with one value.",
            Req("row", OptionKinds.Row, "Top row"), Req("column", OptionKinds.Column, "Left column"),
            Req("height", OptionKinds.Int, "Height"), Req("width", OptionKinds.Int, "Width"), Req("value", OptionKinds.Int, "Value"));
        yield return Op("clear-matrix", "Clear matrix", Edit, "Set every cell to zero.");
        yield return Op("fill-even-odd", "Fill even/odd pattern", Edit, "Fill every cell: where row + column is even it gets 2, otherwise 1.");
        yield return Op("flood-fill", "Flood fill", Edit, "Replace the connected area around a cell with a new value.",
            Req("row", OptionKinds.Row, "Row"), Req("column", OptionKinds.Column, "Column"), Req("newValue", OptionKinds.Int, "New value"));
        yield return Op("place-items", "Place items (bin packing)", Edit, "Pack rectangles onto the grid in the first spot they fit, largest first. Keeps what is already placed.",
            Req("items", OptionKinds.ItemList, "Items (id, width, height)"));
        yield return Op("random-fill", "Random fill", Generate, "Fill every cell with a seeded random value from 0 up to (not including) the limit.",
            Req("seed", OptionKinds.Seed, "Seed"), Opt("maxValue", OptionKinds.Int, "Values below", 2));
        yield return Op("generate-maze-grid", "Generate maze", Generate, "Draw a seeded maze on a new grid (1 = wall, 0 = open). Width and height count maze cells; the grid is (2h+1) x (2w+1).",
            Req("width", OptionKinds.Int, "Width in cells"), Req("height", OptionKinds.Int, "Height in cells"), Req("seed", OptionKinds.Seed, "Seed"));
        yield return Op("transpose", "Transpose", Reshape, "Swap rows and columns.");
        yield return Op("rotate", "Rotate", Reshape, "Rotate clockwise in quarter turns.",
            Opt("quarterTurns", OptionKinds.Int, "Quarter turns", 1));
        yield return Op("flip", "Flip", Reshape, "Mirror the grid left-to-right or top-to-bottom.",
            Pick("axis", "Axis", "horizontal", "horizontal", "vertical"));
        yield return Op("crop", "Crop", Reshape, "Keep only a rectangle of the grid.",
            Req("row", OptionKinds.Row, "Top row"), Req("column", OptionKinds.Column, "Left column"),
            Req("height", OptionKinds.Int, "Height"), Req("width", OptionKinds.Int, "Width"));
        yield return Op("aggregate", "Aggregate", Query, "Sum, minimum, maximum and count over the whole grid, one row or one column.",
            Pick("scope", "Scope", "all", "all", "row", "column"), Opt("index", OptionKinds.Int, "Row or column index", 0));
        yield return Op("find-value", "Find value", Query, "Find every cell that holds a value.",
            Req("value", OptionKinds.Int, "Value"), Opt("limit", OptionKinds.Int, "Most results", 1_000));
        yield return Op("count-regions", "Count regions", Query, "Count connected areas of equal value.",
            Opt("diagonals", OptionKinds.Bool, "Count diagonal neighbours", false),
            Opt("value", OptionKinds.Int, "Only this value (empty = all)", null));
        yield return Op("read-cell", "Read cell", Query, "Read one cell.",
            Req("row", OptionKinds.Row, "Row"), Req("column", OptionKinds.Column, "Column"));
        yield return Op("game-of-life-step", "Game of Life", Query, "Advance Conway's Game of Life by some generations.",
            Opt("generations", OptionKinds.Int, "Generations", 1), Opt("wrap", OptionKinds.Bool, "Wrap around the edges", false));
        yield return Op("grid-shortest-path", "Grid shortest path", Query, "Fewest-moves route between two cells; 0 is open and anything else is a wall.",
            Req("startRow", OptionKinds.Row, "Start row"), Req("startColumn", OptionKinds.Column, "Start column"),
            Req("targetRow", OptionKinds.Row, "Target row"), Req("targetColumn", OptionKinds.Column, "Target column"),
            Opt("diagonals", OptionKinds.Bool, "Allow diagonal moves", false));
        yield return Op("matrix-to-graph", "Matrix to graph", Convert, "Turn the open cells (0) into graph nodes linked to their open neighbours.");

        // ---------- graph ----------
        const string Inspect = "Inspect", Paths = "Paths and flow";

        yield return Op("add-node", "Add nodes", Edit, "Append isolated nodes.",
            Opt("count", OptionKinds.Int, "How many", 1));
        yield return Op("add-connection", "Add connection", Edit, "Connect two nodes, both ways by default or one-way from the first to the second.",
            Req("from", OptionKinds.Node, "From"), Req("to", OptionKinds.Node, "To"), Opt("weight", OptionKinds.Number, "Weight", 1.0),
            Opt("directed", OptionKinds.Bool, "One-way (from to to only)", false));
        yield return Op("remove-connection", "Remove connection", Edit, "Remove the connection between two nodes, both ways by default or only the one-way edge from the first to the second.",
            Req("from", OptionKinds.Node, "From"), Req("to", OptionKinds.Node, "To"),
            Opt("directed", OptionKinds.Bool, "One-way (from to to only)", false));
        yield return Op("set-edge-weight", "Set edge weight", Edit, "Change the weight of an existing connection.",
            Req("from", OptionKinds.Node, "From"), Req("to", OptionKinds.Node, "To"), Req("weight", OptionKinds.Number, "Weight"),
            Opt("symmetric", OptionKinds.Bool, "Update both directions", false));
        yield return Op("isolate-node", "Isolate node", Edit, "Remove every connection of one node.",
            Req("nodeIndex", OptionKinds.Node, "Node"));
        yield return Op("remove-graph-node", "Remove node", Edit, "Delete a node and its connections. Higher node numbers shift down by one; the result lists the old numbers.",
            Req("nodeIndex", OptionKinds.Node, "Node"));
        yield return Op("clear-edges", "Clear edges", Edit, "Remove all connections.",
            Opt("removeNodes", OptionKinds.Bool, "Also remove the nodes", false));
        yield return Op("reverse-edges", "Reverse edges", Edit, "Flip the direction of every connection.");
        yield return Op("subgraph", "Subgraph", Edit, "Keep only the listed nodes and the connections between them.",
            Req("nodes", OptionKinds.NodeList, "Nodes to keep"));
        yield return Op("traverse", "Traverse", Inspect, "Visit nodes breadth-first or depth-first from a start node.",
            Req("start", OptionKinds.Node, "Start"), Pick("order", "Order", "bfs", "bfs", "dfs"));
        yield return Op("degree-stats", "Degree stats", Inspect, "In- and out-degree statistics over the stored edges.");
        yield return Op("has-cycle", "Has cycle", Inspect, "Check whether the graph contains a cycle.",
            Opt("directed", OptionKinds.Bool, "Treat edges as directed", false));
        yield return Op("reachability-count", "Reachability count", Inspect, "Count the nodes reachable from a start node.",
            Req("startNode", OptionKinds.Node, "Start"));
        yield return Op("find-connected-components", "Connected components", Inspect, "Group nodes into connected components.");
        yield return Op("find-articulation-points", "Articulation points", Inspect, "Nodes whose removal disconnects the graph.");
        yield return Op("find-bridges", "Bridges", Inspect, "Edges whose removal disconnects the graph.");
        yield return Op("is-bipartite", "Is bipartite", Inspect, "Check whether the nodes can be split into two groups with no edge inside a group.");
        yield return Op("topological-sort", "Topological sort", Inspect, "Order nodes so every edge points forward (directed acyclic graphs).");
        yield return Op("strongly-connected-components", "Strongly connected components", Inspect, "Groups of nodes that can all reach each other.");
        yield return Op("graph-coloring", "Graph coloring", Inspect, "Color nodes so neighbours differ, using few colors (greedy Welsh-Powell, not guaranteed minimal).");
        yield return Op("centrality", "Centrality", Inspect, "Rank the most important nodes.",
            Pick("measure", "Measure", "betweenness", "betweenness", "closeness", "degree"),
            Opt("k", OptionKinds.Int, "How many to list", 10), Opt("directed", OptionKinds.Bool, "Treat edges as directed", false));
        yield return Op("shortest-path", "Shortest path", Paths, "Cheapest route between two nodes (Dijkstra).",
            Req("start", OptionKinds.Node, "Start"), Req("target", OptionKinds.Node, "Target"));
        yield return Op("bellman-ford", "Shortest path (Bellman-Ford)", Paths, "Cheapest route that also copes with negative weights.",
            Req("start", OptionKinds.Node, "Start"), Req("target", OptionKinds.Node, "Target"));
        yield return Op("all-pairs-shortest-paths", "All-pairs shortest paths", Paths, "Cheapest distance between every pair of nodes (Floyd-Warshall).");
        yield return Op("minimum-spanning-tree", "Minimum spanning tree", Paths, "Cheapest set of connections that links all nodes (Kruskal); a forest if the graph is disconnected.");
        yield return Op("max-flow", "Maximum flow", Paths, "Largest flow from a source to a sink, using weights as capacities (Dinic), plus the minimum cut.",
            Req("source", OptionKinds.Node, "Source"), Req("sink", OptionKinds.Node, "Sink"));
        yield return Op("bipartite-matching", "Bipartite matching", Paths, "Largest set of pairs with no node used twice (graph must be bipartite).");
        yield return Op("settle-debts", "Settle debts", Paths, "Net out debts (A to B means A owes B) into few payments. Greedy heuristic, not a proven minimum.");
        yield return Op("generate-random-graph", "Random graph", Generate, "Replace the graph with a seeded random one.",
            Req("nodes", OptionKinds.Int, "Nodes"), Req("connections", OptionKinds.Int, "Connections"), Req("seed", OptionKinds.Seed, "Seed"),
            Opt("directed", OptionKinds.Bool, "Directed", false), Opt("maxWeight", OptionKinds.Int, "Largest weight (1 = all weights 1)", 1));
        yield return Op("generate-maze", "Maze graph", Generate, "Replace the graph with a seeded maze: one node per cell, exactly one path between any two.",
            Req("width", OptionKinds.Int, "Width in cells"), Req("height", OptionKinds.Int, "Height in cells"), Req("seed", OptionKinds.Seed, "Seed"));
        yield return Op("graph-to-matrix", "Graph to matrix", Convert, "Turn the graph into an adjacency matrix of edge counts (weights are not kept).");

        // ---------- tree ----------
        yield return Op("insert-node", "Insert node", Edit, "Insert a value. Smaller values go left, others right.",
            Req("value", OptionKinds.Int, "Value"));
        yield return Op("remove-node", "Remove node", Edit, "Remove a value, keeping the search order.",
            Req("value", OptionKinds.Int, "Value"));
        yield return Op("tree-clear", "Clear tree", Edit, "Remove every node.");
        yield return Op("tree-rebuild-balanced", "Rebuild balanced", Edit, "Rebuild the tree from its values with the smallest height, dropping dead slots.");
        yield return Op("tree-contains", "Contains", Query, "Check whether a value is in the tree.",
            Req("value", OptionKinds.Int, "Value"));
        yield return Op("tree-traverse", "Traverse", Query, "List the values in pre-, in- or post-order.",
            Pick("order", "Order", "in", "pre", "in", "post"));
        yield return Op("tree-stats", "Stats", Query, "Count, smallest and largest value, height and leaf count.");
        yield return Op("tree-kth-smallest", "K-th smallest", Query, "The k-th smallest value (1 = smallest).",
            Req("k", OptionKinds.Int, "k"));
        yield return Op("tree-lca", "Lowest common ancestor", Query, "The deepest node that has both values below it.",
            Req("a", OptionKinds.Int, "First value"), Req("b", OptionKinds.Int, "Second value"));
        yield return Op("generate-tree", "Random tree", Generate, "Replace the tree with seeded random values below the limit.",
            Req("count", OptionKinds.Int, "Count"), Req("seed", OptionKinds.Seed, "Seed"),
            Opt("maxValue", OptionKinds.Int, "Values below", 1_000_000));
        yield return Op("tree-to-graph", "Tree to graph", Convert, "Turn the tree into a graph.");
    }
}
