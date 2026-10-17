using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using static Engine.Gateway.OperationDsl;

namespace Engine.Gateway;

/// <summary>
/// Operations on a binary search tree. Each entry is the WHOLE definition of one operation: how it appears to a client (label, category,
/// description, options), a valid example, and how its options become an operation. Adding an operation
/// means adding one entry here; nothing else needs to be kept in step.
/// </summary>
internal static class TreeOperationDefinitions
{
    public static IEnumerable<OperationDefinition> All()
    {
        yield return Define.For<Tree>(
            Op("insert-node", "Insert node", Edit, "Insert a value. Smaller values go left, others right.",
                Req("value", OptionKinds.Int, "Value")),
            """{"value":55}""",
            options => new InsertNodeOperation(options["value"].GetInt32()));

        yield return Define.For<Tree>(
            Op("remove-node", "Remove node", Edit, "Remove a value, keeping the search order.",
                Req("value", OptionKinds.Int, "Value")),
            """{"value":30}""",
            options => new RemoveNodeOperation(options["value"].GetInt32()));

        yield return Define.For<Tree>(
            Op("tree-clear", "Clear tree", Edit, "Remove every node."),
            "{}",
            _ => new ClearTreeOperation());

        yield return Define.For<Tree>(
            Op("tree-rebuild-balanced", "Rebuild balanced", Edit, "Rebuild the tree from its values with the smallest height, dropping dead slots."),
            "{}",
            _ => new RebuildBalancedTreeOperation());

        yield return Define.For<Tree>(
            Op("tree-contains", "Contains", Query, "Check whether a value is in the tree.",
                Req("value", OptionKinds.Int, "Value")),
            """{"value":60}""",
            options => new TreeContainsOperation(options["value"].GetInt32()));

        yield return Define.For<Tree>(
            Op("tree-traverse", "Traverse", Query, "List the values in pre-, in- or post-order.",
                Pick("order", "Order", "in", "pre", "in", "post")),
            """{"order":"post"}""",
            options => new TraverseTreeOperation(
                options.TryGetValue("order", out var order) ? order.GetString() ?? "in" : "in"));

        yield return Define.For<Tree>(
            Op("tree-stats", "Stats", Query, "Count, smallest and largest value, height and leaf count."),
            "{}",
            _ => new TreeStatsOperation());

        yield return Define.For<Tree>(
            Op("tree-kth-smallest", "K-th smallest", Query, "The k-th smallest value (1 = smallest).",
                Req("k", OptionKinds.Int, "k")),
            """{"k":3}""",
            options => new TreeKthSmallestOperation(options["k"].GetInt32()));

        yield return Define.For<Tree>(
            Op("tree-lca", "Lowest common ancestor", Query, "The deepest node that has both values below it.",
                Req("a", OptionKinds.Int, "First value"), Req("b", OptionKinds.Int, "Second value")),
            """{"a":20,"b":40}""",
            options => new TreeLowestCommonAncestorOperation(options["a"].GetInt32(), options["b"].GetInt32()));

        yield return Define.For<Tree>(
            Op("generate-tree", "Random tree", Generate, "Replace the tree with seeded random values below the limit.",
                Req("count", OptionKinds.Int, "Count"), Req("seed", OptionKinds.Seed, "Seed"),
                Opt("maxValue", OptionKinds.Int, "Values below", 1_000_000)),
            """{"count":10,"seed":1,"maxValue":1000}""",
            options => new GenerateTreeOperation(
                options["count"].GetInt32(),
                options["seed"].GetInt32(),
                options.TryGetValue("maxValue", out var maxValue) ? maxValue.GetInt32() : 1_000_000));

        yield return Define.For<Tree>(
            Op("tree-to-graph", "Tree to graph", Conversion, "Turn the tree into a graph."),
            "{}",
            _ => new TreeToGraphOperation());
    }
}
