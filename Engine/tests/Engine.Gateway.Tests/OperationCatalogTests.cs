using Engine.Gateway;
using Xunit;

namespace Engine.Gateway.Tests;

public class OperationCatalogTests
{
    [Theory]
    [InlineData("set-cell", "matrix")]
    [InlineData("fill-region", "matrix")]
    [InlineData("clear-matrix", "matrix")]
    [InlineData("transpose", "matrix")]
    [InlineData("rotate", "matrix")]
    [InlineData("flip", "matrix")]
    [InlineData("crop", "matrix")]
    [InlineData("aggregate", "matrix")]
    [InlineData("find-value", "matrix")]
    [InlineData("count-regions", "matrix")]
    [InlineData("game-of-life-step", "matrix")]
    [InlineData("grid-shortest-path", "matrix")]
    [InlineData("generate-maze-grid", "matrix")]
    [InlineData("random-fill", "matrix")]
    [InlineData("fill-even-odd", "matrix")]
    [InlineData("read-cell", "matrix")]
    [InlineData("flood-fill", "matrix")]
    [InlineData("place-items", "matrix")]
    [InlineData("matrix-to-graph", "matrix")]
    [InlineData("generate-maze", "graph")]
    [InlineData("add-node", "graph")]
    [InlineData("set-edge-weight", "graph")]
    [InlineData("clear-edges", "graph")]
    [InlineData("reverse-edges", "graph")]
    [InlineData("subgraph", "graph")]
    [InlineData("traverse", "graph")]
    [InlineData("degree-stats", "graph")]
    [InlineData("has-cycle", "graph")]
    [InlineData("graph-to-matrix", "graph")]
    [InlineData("reachability-count", "graph")]
    [InlineData("shortest-path", "graph")]
    [InlineData("minimum-spanning-tree", "graph")]
    [InlineData("settle-debts", "graph")]
    [InlineData("add-connection", "graph")]
    [InlineData("remove-connection", "graph")]
    [InlineData("isolate-node", "graph")]
    [InlineData("remove-graph-node", "graph")]
    [InlineData("find-articulation-points", "graph")]
    [InlineData("find-bridges", "graph")]
    [InlineData("find-connected-components", "graph")]
    [InlineData("is-bipartite", "graph")]
    [InlineData("topological-sort", "graph")]
    [InlineData("strongly-connected-components", "graph")]
    [InlineData("bellman-ford", "graph")]
    [InlineData("all-pairs-shortest-paths", "graph")]
    [InlineData("max-flow", "graph")]
    [InlineData("bipartite-matching", "graph")]
    [InlineData("centrality", "graph")]
    [InlineData("graph-coloring", "graph")]
    [InlineData("generate-random-graph", "graph")]
    [InlineData("insert-node", "tree")]
    [InlineData("remove-node", "tree")]
    [InlineData("tree-contains", "tree")]
    [InlineData("tree-traverse", "tree")]
    [InlineData("tree-stats", "tree")]
    [InlineData("tree-kth-smallest", "tree")]
    [InlineData("tree-lca", "tree")]
    [InlineData("tree-clear", "tree")]
    [InlineData("tree-rebuild-balanced", "tree")]
    [InlineData("tree-to-graph", "tree")]
    [InlineData("generate-tree", "tree")]
    public void BuildDefault_EveryOperation_HasExpectedDatasetType(string operationId, string expectedDatasetType)
    {
        var registry = OperationCatalog.BuildDefault();
        var handler = registry.TryGet(operationId);

        Assert.NotNull(handler);
        Assert.Equal(expectedDatasetType, handler!.DatasetType);
    }
}