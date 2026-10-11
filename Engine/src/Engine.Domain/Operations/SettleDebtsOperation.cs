using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

/// <summary>
/// Nets out a graph of directed debts (edge A→B, weight W means "A owes B W")
/// into a minimized set of settlement transactions, via greedy largest-debtor/
/// largest-creditor matching. This is a well-known heuristic, not a proven
/// minimum — an exact minimum-transaction solution is NP-hard in general.
/// Deterministic given the same input (required for rehydration replay):
/// ties are broken by ascending node id.
/// </summary>
public sealed class SettleDebtsOperation : IOperation<Graph>
{
    private const double Epsilon = 1e-9;
    public sealed record BalanceComputedPayload(int Node, double Balance);
    public sealed record TransactionCreatedPayload(int From, int To, double Amount);
    public OperationMetadata Metadata { get; } = new("settle-debts", StateEffect.ReplacesState);

    public OperationExecutionResult Execute(Graph input, OperationContext context)
    {
        int n = input.NodeCount;
        var balance = new double[n];
        var trace = new TraceRecorder(context);

        for (int node = 0; node < n; node++)
            foreach (var (neighbor, weight) in input.GetWeightedNeighbors(node))
            {
                balance[node] -= weight;   // node owes weight...
                balance[neighbor] += weight; // ...to neighbor
            }

        for (int node = 0; node < n; node++)
            trace.Add(new DomainStepEvent("BalanceComputed", $"Node {node}: {balance[node]:0.##}",
                new BalanceComputedPayload(node, balance[node])));
        var debtors = new List<int>();
        var creditors = new List<int>();
        for (int node = 0; node < n; node++)
        {
            if (balance[node] < -Epsilon) debtors.Add(node);
            else if (balance[node] > Epsilon) creditors.Add(node);
        }

        // Deterministic ordering: largest magnitude first, ties broken by node id.
        debtors.Sort((a, b) => balance[a] != balance[b] ? balance[a].CompareTo(balance[b]) : a.CompareTo(b));
        creditors.Sort((a, b) => balance[b] != balance[a] ? balance[b].CompareTo(balance[a]) : a.CompareTo(b));

        var transactions = new List<(int From, int To, double Weight)>();
        int di = 0, ci = 0;
        while (di < debtors.Count && ci < creditors.Count)
        {
            int debtor = debtors[di];
            int creditor = creditors[ci];
            double amount = Math.Min(-balance[debtor], balance[creditor]);

            transactions.Add((debtor, creditor, amount));
            trace.Add(new DomainStepEvent("TransactionCreated", $"{debtor} pays {creditor} {amount:0.##}",
                new TransactionCreatedPayload(debtor, creditor, amount)));
            balance[debtor] += amount;
            balance[creditor] -= amount;

            if (Math.Abs(balance[debtor]) < Epsilon) di++;
            if (Math.Abs(balance[creditor]) < Epsilon) ci++;
        }

        return new OperationExecutionResult(
            Succeeded: true,
            NewState: new Graph(n, transactions),
            Value: null,
            Trace: trace,
            FailureReason: null);
    }
}