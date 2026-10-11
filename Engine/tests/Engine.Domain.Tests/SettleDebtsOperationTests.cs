using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class SettleDebtsOperationTests
{
    [Fact]
    public void Execute_SingleDebt_ProducesOneMatchingTransaction()
    {
        var graph = new Graph(nodeCount: 2, edges: [(0, 1, 100.0)]); // node 0 owes node 1, 100

        var result = new SettleDebtsOperation().Execute(graph);

        var settlement = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(100.0, settlement.GetWeight(0, 1));
    }

    [Fact]
    public void Execute_MutualEqualDebts_ProduceNoTransactions()
    {
        var graph = new Graph(nodeCount: 2, edges: [(0, 1, 100.0), (1, 0, 100.0)]); // cancels out exactly

        var result = new SettleDebtsOperation().Execute(graph);

        var settlement = Assert.IsType<Graph>(result.NewState);
        Assert.Empty(settlement.GetNeighbors(0));
        Assert.Empty(settlement.GetNeighbors(1));
    }

    [Fact]
    public void Execute_ChainOfDebts_SimplifiesToSingleTransaction()
    {
        // A owes B 30, B owes C 30, A owes C 30 — B nets to zero, A owes C 60 overall
        var graph = new Graph(nodeCount: 3, edges: [(0, 1, 30.0), (1, 2, 30.0), (0, 2, 30.0)]);

        var result = new SettleDebtsOperation().Execute(graph);

        var settlement = Assert.IsType<Graph>(result.NewState);
        Assert.Equal(60.0, settlement.GetWeight(0, 2)); // 3 original debts simplified to 1
        Assert.Empty(settlement.GetNeighbors(1)); // node 1 (B) settled out entirely
    }

    [Fact]
    public void Execute_SettlementBalances_MatchOriginalBalances_ForArbitraryDebtGraph()
    {
        var graph = new Graph(nodeCount: 4, edges: [(0, 2, 30.0), (0, 3, 10.0), (1, 3, 20.0)]);

        var originalBalance = ComputeBalances(graph, nodeCount: 4);

        var result = new SettleDebtsOperation().Execute(graph);
        var settlement = Assert.IsType<Graph>(result.NewState);
        var settlementBalance = ComputeBalances(settlement, nodeCount: 4);

        Assert.Equal(originalBalance, settlementBalance);
    }

    private static double[] ComputeBalances(Graph graph, int nodeCount)
    {
        var balance = new double[nodeCount];
        for (int node = 0; node < nodeCount; node++)
            foreach (var (neighbor, weight) in graph.GetWeightedNeighbors(node))
            {
                balance[node] -= weight;
                balance[neighbor] += weight;
            }
        return balance;
    }

    [Fact]
    public void Execute_SameInput_ProducesIdenticalTransactionSet()
    {
        var graph = new Graph(nodeCount: 4, edges: [(0, 2, 30.0), (0, 3, 10.0), (1, 3, 20.0)]);

        var a = new SettleDebtsOperation().Execute(graph);
        var b = new SettleDebtsOperation().Execute(graph);

        Assert.Equal(EdgeSet(a.NewState), EdgeSet(b.NewState)); // determinism — required for rehydration replay
    }

    private static HashSet<(int, int, double)> EdgeSet(IDataset? dataset)
    {
        var graph = Assert.IsType<Graph>(dataset);
        var set = new HashSet<(int, int, double)>();
        for (int i = 0; i < graph.NodeCount; i++)
            foreach (var (neighbor, weight) in graph.GetWeightedNeighbors(i))
                set.Add((i, neighbor, weight));
        return set;
    }
    [Fact]
    public void Constructor_EdgeReferencesOutOfRangeNode_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Graph(4, [(4, 1, 1.0)]));
    }
}