using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

/// <summary>
/// Operations that walk the whole dataset but record few trace events must still be stopped by the
/// work budget. Each case uses an input where the answer is "nothing found" (a ring has no bridges and
/// one component), so the trace contributes almost nothing and only an explicit charge can trigger.
/// </summary>
public class SparseTraceBudgetTests
{
    private const int Nodes = 2_000;

    private static Graph Ring()
    {
        var edges = new List<(int, int)>();
        for (int i = 0; i < Nodes; i++)
        {
            int next = (i + 1) % Nodes;
            edges.Add((i, next));
            edges.Add((next, i));
        }
        return new Graph(Nodes, edges);
    }

    private static OperationContext TinyBudget() => new(CancellationToken.None, timeLimit: null, maxWorkUnits: 100);

    private static void AssertBudgetExceeded(Action run)
    {
        var aborted = Assert.Throws<OperationAbortedException>(run);
        Assert.Equal(OperationAbortReason.WorkBudgetExceeded, aborted.Reason);
    }

    [Fact] public void FindBridges_OnBridgelessGraph() =>
        AssertBudgetExceeded(() => new FindBridgesOperation().Execute(Ring(), TinyBudget()));

    [Fact] public void FindArticulationPoints_OnGraphWithNone() =>
        AssertBudgetExceeded(() => new FindArticulationPointsOperation().Execute(Ring(), TinyBudget()));

    [Fact] public void FindConnectedComponents_OnSingleComponent() =>
        AssertBudgetExceeded(() => new FindConnectedComponentsOperation().Execute(Ring(), TinyBudget()));

    [Fact] public void AddConnection_RebuildsTheGraph() =>
        AssertBudgetExceeded(() => new AddConnectionOperation(0, Nodes / 2).Execute(Ring(), TinyBudget()));

    [Fact] public void RemoveConnection_RebuildsTheGraph() =>
        AssertBudgetExceeded(() => new RemoveConnectionOperation(0, 1).Execute(Ring(), TinyBudget()));

    [Fact] public void IsolateNode_RebuildsTheGraph() =>
        AssertBudgetExceeded(() => new IsolateNodeOperation(0).Execute(Ring(), TinyBudget()));

    [Fact] public void TreeStats_WalksEveryNode()
    {
        var tree = Tree.Empty();
        foreach (int value in Enumerable.Range(0, 500).OrderBy(v => (v * 7919) % 500)) // roughly balanced: few levels
            tree = (Tree)new InsertNodeOperation(value).Execute(tree).NewState!;

        AssertBudgetExceeded(() => new TreeStatsOperation().Execute(tree, TinyBudget()));
    }
}
