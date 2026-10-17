using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using static Engine.Gateway.OperationDsl;

namespace Engine.Gateway;

/// <summary>
/// Operations on a weighted graph (nodes are 0..n-1; an undirected connection is two stored edges). Each entry is the WHOLE definition of one operation: how it appears to a client (label, category,
/// description, options), a valid example, and how its options become an operation. Adding an operation
/// means adding one entry here; nothing else needs to be kept in step.
/// </summary>
internal static class GraphOperationDefinitions
{
    public static IEnumerable<OperationDefinition> All()
    {
        yield return Define.For<Graph>(
            Op("add-node", "Add nodes", Edit, "Append isolated nodes.",
                Opt("count", OptionKinds.Int, "How many", 1)),
            """{"count":1}""",
            options => new AddNodeOperation(options.TryGetValue("count", out var count) ? count.GetInt32() : 1));

        yield return Define.For<Graph>(
            Op("add-connection", "Add connection", Edit, "Connect two nodes, both ways by default or one-way from the first to the second.",
                Req("from", OptionKinds.Node, "From"), Req("to", OptionKinds.Node, "To"), Opt("weight", OptionKinds.Number, "Weight", 1.0),
                Opt("directed", OptionKinds.Bool, "One-way (from to to only)", false)),
            """{"from":0,"to":7,"weight":2,"directed":false}""",
            options => new AddConnectionOperation(
                options["from"].GetInt32(), options["to"].GetInt32(),
                options.TryGetValue("weight", out var w) ? w.GetDouble() : 1.0,
                options.TryGetValue("directed", out var directedAdd) && directedAdd.GetBoolean()));

        yield return Define.For<Graph>(
            Op("remove-connection", "Remove connection", Edit, "Remove the connection between two nodes, both ways by default or only the one-way edge from the first to the second.",
                Req("from", OptionKinds.Node, "From"), Req("to", OptionKinds.Node, "To"),
                Opt("directed", OptionKinds.Bool, "One-way (from to to only)", false)),
            """{"from":0,"to":1,"directed":false}""",
            options => new RemoveConnectionOperation(
                options["from"].GetInt32(), options["to"].GetInt32(),
                options.TryGetValue("directed", out var directedRemove) && directedRemove.GetBoolean()));

        yield return Define.For<Graph>(
            Op("set-edge-weight", "Set edge weight", Edit, "Change the weight of an existing connection.",
                Req("from", OptionKinds.Node, "From"), Req("to", OptionKinds.Node, "To"), Req("weight", OptionKinds.Number, "Weight"),
                Opt("symmetric", OptionKinds.Bool, "Update both directions", false)),
            """{"from":0,"to":1,"weight":3,"symmetric":true}""",
            options => new SetEdgeWeightOperation(
                options["from"].GetInt32(), options["to"].GetInt32(), options["weight"].GetDouble(),
                options.TryGetValue("symmetric", out var symmetric) && symmetric.GetBoolean()));

        yield return Define.For<Graph>(
            Op("isolate-node", "Isolate node", Edit, "Remove every connection of one node.",
                Req("nodeIndex", OptionKinds.Node, "Node")),
            """{"nodeIndex":0}""",
            options => new IsolateNodeOperation(options["nodeIndex"].GetInt32()));

        yield return Define.For<Graph>(
            Op("remove-graph-node", "Remove node", Edit, "Delete a node and its connections. Higher node numbers shift down by one; the result lists the old numbers.",
                Req("nodeIndex", OptionKinds.Node, "Node")),
            """{"nodeIndex":0}""",
            options => new RemoveGraphNodeOperation(options["nodeIndex"].GetInt32()));

        yield return Define.For<Graph>(
            Op("clear-edges", "Clear edges", Edit, "Remove all connections.",
                Opt("removeNodes", OptionKinds.Bool, "Also remove the nodes", false)),
            """{"removeNodes":false}""",
            options => new ClearEdgesOperation(
                options.TryGetValue("removeNodes", out var removeNodes) && removeNodes.GetBoolean()));

        yield return Define.For<Graph>(
            Op("reverse-edges", "Reverse edges", Edit, "Flip the direction of every connection."),
            "{}",
            _ => new ReverseEdgesOperation());

        yield return Define.For<Graph>(
            Op("subgraph", "Subgraph", Edit, "Keep only the listed nodes and the connections between them.",
                Req("nodes", OptionKinds.NodeList, "Nodes to keep")),
            """{"nodes":[0,1,2,3]}""",
            options => new SubgraphOperation(options["nodes"].EnumerateArray().Select(e => e.GetInt32()).ToList()));

        yield return Define.For<Graph>(
            Op("traverse", "Traverse", Inspect, "Visit nodes breadth-first or depth-first from a start node.",
                Req("start", OptionKinds.Node, "Start"), Pick("order", "Order", "bfs", "bfs", "dfs")),
            """{"start":0,"order":"bfs"}""",
            options => new TraverseGraphOperation(
                options["start"].GetInt32(),
                options.TryGetValue("order", out var order) ? order.GetString() ?? "bfs" : "bfs"));

        yield return Define.For<Graph>(
            Op("degree-stats", "Degree stats", Inspect, "In- and out-degree statistics over the stored edges."),
            "{}",
            _ => new DegreeStatsOperation());

        yield return Define.For<Graph>(
            Op("has-cycle", "Has cycle", Inspect, "Check whether the graph contains a cycle.",
                Opt("directed", OptionKinds.Bool, "Treat edges as directed", false)),
            """{"directed":false}""",
            options => new HasCycleOperation(
                options.TryGetValue("directed", out var directed) && directed.GetBoolean()));

        yield return Define.For<Graph>(
            Op("reachability-count", "Reachability count", Inspect, "Count the nodes reachable from a start node.",
                Req("startNode", OptionKinds.Node, "Start")),
            """{"startNode":0}""",
            options => new ReachabilityQuery(options["startNode"].GetInt32()));

        yield return Define.For<Graph>(
            Op("find-connected-components", "Connected components", Inspect, "Group nodes into connected components."),
            "{}",
            _ => new FindConnectedComponentsOperation());

        yield return Define.For<Graph>(
            Op("find-articulation-points", "Articulation points", Inspect, "Nodes whose removal disconnects the graph."),
            "{}",
            _ => new FindArticulationPointsOperation());

        yield return Define.For<Graph>(
            Op("find-bridges", "Bridges", Inspect, "Edges whose removal disconnects the graph."),
            "{}",
            _ => new FindBridgesOperation());

        yield return Define.For<Graph>(
            Op("is-bipartite", "Is bipartite", Inspect, "Check whether the nodes can be split into two groups with no edge inside a group."),
            "{}",
            _ => new IsBipartiteOperation());

        yield return Define.For<Graph>(
            Op("topological-sort", "Topological sort", Inspect, "Order nodes so every edge points forward (directed acyclic graphs)."),
            "{}",
            _ => new TopologicalSortOperation());

        yield return Define.For<Graph>(
            Op("strongly-connected-components", "Strongly connected components", Inspect, "Groups of nodes that can all reach each other."),
            "{}",
            _ => new StronglyConnectedComponentsOperation());

        yield return Define.For<Graph>(
            Op("graph-coloring", "Graph coloring", Inspect, "Color nodes so neighbours differ, using few colors (greedy Welsh-Powell, not guaranteed minimal)."),
            "{}",
            _ => new GraphColoringOperation());

        yield return Define.For<Graph>(
            Op("centrality", "Centrality", Inspect, "Rank the most important nodes.",
                Pick("measure", "Measure", "betweenness", "betweenness", "closeness", "degree"),
                Opt("k", OptionKinds.Int, "How many to list", 10), Opt("directed", OptionKinds.Bool, "Treat edges as directed", false)),
            """{"measure":"betweenness","k":3,"directed":false}""",
            options => new CentralityOperation(
                options.TryGetValue("measure", out var measure) ? measure.GetString() ?? "betweenness" : "betweenness",
                options.TryGetValue("k", out var k) ? k.GetInt32() : 10,
                options.TryGetValue("directed", out var directedScores) && directedScores.GetBoolean()));

        yield return Define.For<Graph>(
            Op("shortest-path", "Shortest path", Paths, "Cheapest route between two nodes (Dijkstra).",
                Req("start", OptionKinds.Node, "Start"), Req("target", OptionKinds.Node, "Target")),
            """{"start":0,"target":7}""",
            options => new ShortestPathOperation(options["start"].GetInt32(), options["target"].GetInt32()));

        yield return Define.For<Graph>(
            Op("bellman-ford", "Shortest path (Bellman-Ford)", Paths, "Cheapest route that also copes with negative weights.",
                Req("start", OptionKinds.Node, "Start"), Req("target", OptionKinds.Node, "Target")),
            """{"start":0,"target":7}""",
            options => new BellmanFordOperation(options["start"].GetInt32(), options["target"].GetInt32()));

        yield return Define.For<Graph>(
            Op("all-pairs-shortest-paths", "All-pairs shortest paths", Paths, "Cheapest distance between every pair of nodes (Floyd-Warshall)."),
            "{}",
            _ => new AllPairsShortestPathsOperation());

        yield return Define.For<Graph>(
            Op("minimum-spanning-tree", "Minimum spanning tree", Paths, "Cheapest set of connections that links all nodes (Kruskal); a forest if the graph is disconnected."),
            "{}",
            _ => new MinimumSpanningTreeOperation());

        yield return Define.For<Graph>(
            Op("max-flow", "Maximum flow", Paths, "Largest flow from a source to a sink, using weights as capacities (Dinic), plus the minimum cut.",
                Req("source", OptionKinds.Node, "Source"), Req("sink", OptionKinds.Node, "Sink")),
            """{"source":0,"sink":7}""",
            options => new MaxFlowOperation(options["source"].GetInt32(), options["sink"].GetInt32()));

        yield return Define.For<Graph>(
            Op("bipartite-matching", "Bipartite matching", Paths, "Largest set of pairs with no node used twice (graph must be bipartite)."),
            "{}",
            _ => new BipartiteMatchingOperation());

        yield return Define.For<Graph>(
            Op("settle-debts", "Settle debts", Paths, "Net out debts (A to B means A owes B) into few payments. Greedy heuristic, not a proven minimum."),
            "{}",
            _ => new SettleDebtsOperation());

        yield return Define.For<Graph>(
            Op("generate-random-graph", "Random graph", Generate, "Replace the graph with a seeded random one.",
                Req("nodes", OptionKinds.Int, "Nodes"), Req("connections", OptionKinds.Int, "Connections"), Req("seed", OptionKinds.Seed, "Seed"),
                Opt("directed", OptionKinds.Bool, "Directed", false), Opt("maxWeight", OptionKinds.Int, "Largest weight (1 = all weights 1)", 1)),
            """{"nodes":6,"connections":5,"seed":2,"directed":false,"maxWeight":4}""",
            options => new GenerateRandomGraphOperation(
                options["nodes"].GetInt32(), options["connections"].GetInt32(), options["seed"].GetInt32(),
                options.TryGetValue("directed", out var directedGraph) && directedGraph.GetBoolean(),
                options.TryGetValue("maxWeight", out var maxWeight) ? maxWeight.GetInt32() : 1));

        yield return Define.For<Graph>(
            Op("generate-maze", "Maze graph", Generate, "Replace the graph with a seeded maze: one node per cell, exactly one path between any two.",
                Req("width", OptionKinds.Int, "Width in cells"), Req("height", OptionKinds.Int, "Height in cells"), Req("seed", OptionKinds.Seed, "Seed")),
            """{"width":4,"height":4,"seed":1}""",
            options => new GenerateMazeOperation(
                options["width"].GetInt32(), options["height"].GetInt32(), options["seed"].GetInt32()));

        yield return Define.For<Graph>(
            Op("graph-to-matrix", "Graph to matrix", Conversion, "Turn the graph into an adjacency matrix of edge counts (weights are not kept)."),
            "{}",
            _ => new GraphToMatrixOperation());
    }
}
