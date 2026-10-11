using System.Text.Json;

namespace Engine.Gateway;

/// <summary>
/// One valid options object per operation, including every optional key. Doubles as documentation
/// (served by GET /operations) and as the input for the contract tests. The sample datasets those
/// tests use: a 6x8 matrix, an 8-node weighted undirected graph, and the tree 50,30,70,20,40,60,80.
/// </summary>
public static class OperationExamples
{
    private static readonly Dictionary<string, string> Examples = new()
    {
        // matrix
        ["set-cell"] = """{"row":0,"column":0,"value":7}""",
        ["fill-region"] = """{"row":1,"column":1,"height":2,"width":2,"value":9}""",
        ["clear-matrix"] = "{}",
        ["transpose"] = "{}",
        ["rotate"] = """{"quarterTurns":1}""",
        ["flip"] = """{"axis":"vertical"}""",
        ["crop"] = """{"row":1,"column":1,"height":3,"width":4}""",
        ["aggregate"] = """{"scope":"row","index":1}""",
        ["find-value"] = """{"value":1,"limit":100}""",
        ["count-regions"] = """{"diagonals":false,"value":0}""",
        ["game-of-life-step"] = """{"generations":3,"wrap":false}""",
        ["grid-shortest-path"] = """{"startRow":0,"startColumn":0,"targetRow":1,"targetColumn":1,"diagonals":false}""",
        ["generate-maze-grid"] = """{"width":4,"height":3,"seed":1}""",
        ["random-fill"] = """{"seed":1,"maxValue":2}""",
        ["fill-even-odd"] = "{}",
        ["read-cell"] = """{"row":0,"column":0}""",
        ["flood-fill"] = """{"row":0,"column":0,"newValue":5}""",
        ["place-items"] = """{"items":[{"id":1,"width":2,"height":2}]}""",
        ["matrix-to-graph"] = "{}",

        // graph
        ["add-node"] = """{"count":1}""",
        ["set-edge-weight"] = """{"from":0,"to":1,"weight":3,"symmetric":true}""",
        ["clear-edges"] = """{"removeNodes":false}""",
        ["reverse-edges"] = "{}",
        ["subgraph"] = """{"nodes":[0,1,2,3]}""",
        ["traverse"] = """{"start":0,"order":"bfs"}""",
        ["degree-stats"] = "{}",
        ["has-cycle"] = """{"directed":false}""",
        ["graph-to-matrix"] = "{}",
        ["generate-maze"] = """{"width":4,"height":4,"seed":1}""",
        ["reachability-count"] = """{"startNode":0}""",
        ["shortest-path"] = """{"start":0,"target":7}""",
        ["minimum-spanning-tree"] = "{}",
        ["settle-debts"] = "{}",
        ["add-connection"] = """{"from":0,"to":7,"weight":2}""",
        ["remove-connection"] = """{"from":0,"to":1}""",
        ["isolate-node"] = """{"nodeIndex":0}""",
        ["find-articulation-points"] = "{}",
        ["find-bridges"] = "{}",
        ["find-connected-components"] = "{}",
        ["is-bipartite"] = "{}",
        ["topological-sort"] = "{}",
        ["strongly-connected-components"] = "{}",
        ["bellman-ford"] = """{"start":0,"target":7}""",
        ["all-pairs-shortest-paths"] = "{}",
        ["max-flow"] = """{"source":0,"sink":7}""",
        ["bipartite-matching"] = "{}",
        ["centrality"] = """{"measure":"betweenness","k":3,"directed":false}""",
        ["graph-coloring"] = "{}",
        ["generate-random-graph"] = """{"nodes":6,"connections":5,"seed":2,"directed":false,"maxWeight":4}""",

        // tree
        ["insert-node"] = """{"value":55}""",
        ["remove-node"] = """{"value":30}""",
        ["tree-contains"] = """{"value":60}""",
        ["tree-traverse"] = """{"order":"post"}""",
        ["tree-stats"] = "{}",
        ["tree-kth-smallest"] = """{"k":3}""",
        ["tree-lca"] = """{"a":20,"b":40}""",
        ["tree-clear"] = "{}",
        ["tree-rebuild-balanced"] = "{}",
        ["tree-to-graph"] = "{}",
        ["generate-tree"] = """{"count":10,"seed":1,"maxValue":1000}""",
    };

    public static IEnumerable<string> OperationIds => Examples.Keys;

    public static JsonElement? TryGetElement(string operationId)
    {
        if (!Examples.TryGetValue(operationId, out var text)) return null;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>The example as an options dictionary, ready to hand to a handler.</summary>
    public static Dictionary<string, JsonElement> Parse(string operationId)
    {
        using var document = JsonDocument.Parse(Examples[operationId]);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}