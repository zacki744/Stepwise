using System.Text;
using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Domain.Tracing;
using Engine.Protocol;
using Engine.Session;
using Xunit;

namespace Engine.Gateway.Tests;

/// <summary>
/// Rules every registered operation must obey, checked across the whole catalog:
///  - valid input succeeds and never mutates the dataset it was given
///  - the same input twice gives byte-identical results (required for rehydration replay)
///  - hostile options (wrong types, huge/negative numbers, nulls, missing keys) fail only with the
///    exceptions the gateway treats as client input, and a failure leaves the session untouched
///  - every operation consults its work budget (so none can run unbounded)
/// </summary>
public class OperationContractTests
{
    private static readonly OperationRegistry Registry = OperationCatalog.BuildDefault();

    // Genuinely O(1) work, so it has nothing to charge a budget for.
    private static readonly HashSet<string> ConstantTimeOperations = ["read-cell"];

    private static readonly string[] HostileValues =
        ["-1", "0", "1", "2147483647", "-2147483648", "2.5", "\"text\"", "null", "true", "[]", "{}", "[1,2,3]"];

    public static IEnumerable<object[]> AllOperationIds() =>
        Registry.All.OrderBy(h => h.OperationId).Select(h => new object[] { h.OperationId });

    // ---------- sample datasets ----------

    internal static IDataset NewDataset(string datasetType) => datasetType switch
    {
        "matrix" => SampleMatrix(),
        "graph" => SampleGraph(),
        "tree" => SampleTree(),
        _ => throw new InvalidOperationException($"No sample dataset for '{datasetType}'")
    };

    private static Matrix SampleMatrix()
    {
        var matrix = new Matrix(6, 8);
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 8; c++)
                matrix[r, c] = (r * 5 + c * 3) % 4;
        return matrix;
    }

    private static Graph SampleGraph() =>
        (Graph)new GenerateRandomGraphOperation(8, 12, seed: 1, maxWeight: 9).Execute(Graph.Empty(1)).NewState!;

    private static Tree SampleTree()
    {
        var tree = Tree.Empty();
        foreach (var value in new[] { 50, 30, 70, 20, 40, 60, 80 })
            tree = (Tree)new InsertNodeOperation(value).Execute(tree).NewState!;
        return tree;
    }

    internal static string Fingerprint(IDataset dataset)
    {
        var text = new StringBuilder();
        switch (dataset)
        {
            case Matrix matrix:
                text.Append($"matrix {matrix.Rows}x{matrix.Columns}:");
                for (int r = 0; r < matrix.Rows; r++)
                    for (int c = 0; c < matrix.Columns; c++)
                        text.Append(matrix[r, c]).Append(',');
                break;
            case Graph graph:
                text.Append($"graph {graph.NodeCount}:");
                foreach (var (from, to, weight) in graph.AllDirectedEdges())
                    text.Append($"{from}>{to}@{weight:R};");
                break;
            case Tree tree:
                text.Append($"tree slots={tree.SlotCount} root={tree.RootIndex}:");
                for (int i = 0; i < tree.SlotCount; i++)
                    text.Append($"{i}={tree.GetValue(i)}({tree.LeftChild(i)},{tree.RightChild(i)});");
                break;
        }
        return text.ToString();
    }

    // ---------- running one operation ----------

    private sealed class Run
    {
        public Exception? Error;
        public OperationOutcome? Outcome;
        public IDataset Before = null!;
        public string BeforeFingerprint = "";
        public string BeforeFingerprintAfterRun = "";
        public IDataset SessionAfter = null!;
        public string? ValueJson;
    }

    private static Run Execute(IOperationHandler handler, IReadOnlyDictionary<string, JsonElement> options, OperationContext? context = null)
    {
        var sessions = new SessionManager(maxSessions: 1);
        var before = NewDataset(handler.DatasetType);
        var session = sessions.Create(before);
        var run = new Run { Before = before, BeforeFingerprint = Fingerprint(before) };

        try
        {
            run.Outcome = handler.Execute(sessions, session.Id, options,
                context ?? new OperationContext(CancellationToken.None, TimeSpan.FromSeconds(10), maxWorkUnits: 20_000_000));
        }
        catch (Exception ex)
        {
            run.Error = ex;
        }

        run.SessionAfter = session.Dataset;
        run.BeforeFingerprintAfterRun = Fingerprint(before);
        if (run.Outcome?.Value is not null)
            run.ValueJson = JsonSerializer.Serialize(run.Outcome.Value, ProtocolJson.Options);
        return run;
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    // The failures the gateway already reports to the client as a normal error (plus the work-budget abort).
    private static bool IsAcceptable(Exception ex) =>
        ex is FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException
            or OperationAbortedException;

    // ---------- the rules ----------

    [Fact]
    public void EveryOperationHasAnExample_AndNoExampleIsStale()
    {
        var registered = Registry.All.Select(h => h.OperationId).ToHashSet();
        var exampled = OperationExamples.OperationIds.ToHashSet();

        Assert.Empty(registered.Except(exampled)); // registered but no example: add one to OperationExamples
        Assert.Empty(exampled.Except(registered)); // example for an operation that no longer exists
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void ValidExample_Succeeds_WithoutMutatingItsInput(string id)
    {
        var run = Execute(Registry.TryGet(id)!, OperationExamples.Parse(id));

        Assert.True(run.Error is null, $"{id} failed on its own example: {run.Error}");
        Assert.True(run.Outcome!.Succeeded);
        Assert.Equal(run.BeforeFingerprint, run.BeforeFingerprintAfterRun); // never modified in place

        if (run.Outcome.CommittedState is null) Assert.Same(run.Before, run.SessionAfter);        // queries leave state alone
        else Assert.Same(run.Outcome.CommittedState, run.SessionAfter);                           // commits are what the outcome says
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void SameInputTwice_GivesIdenticalResults(string id)
    {
        var handler = Registry.TryGet(id)!;
        var options = OperationExamples.Parse(id);

        var first = Execute(handler, options);
        var second = Execute(handler, options);

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.Equal(Fingerprint(first.SessionAfter), Fingerprint(second.SessionAfter));
        Assert.Equal(first.ValueJson, second.ValueJson);
        Assert.Equal(first.Outcome!.Trace.Select(e => e.Description), second.Outcome!.Trace.Select(e => e.Description));
        Assert.Equal(TraceSummary.Of(first.Outcome.Trace).TotalCount, TraceSummary.Of(second.Outcome.Trace).TotalCount);
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void HostileOptions_NeverCauseAnUnexpectedFailure(string id)
    {
        var handler = Registry.TryGet(id)!;
        var valid = OperationExamples.Parse(id);

        var attempts = new List<(string Label, Dictionary<string, JsonElement> Options)> { ("no options at all", new()) };
        foreach (var key in valid.Keys)
            foreach (var text in HostileValues)
                attempts.Add(($"{key} = {text}", new Dictionary<string, JsonElement>(valid) { [key] = Json(text) }));
        foreach (var text in HostileValues)
            attempts.Add(($"every option = {text}", valid.Keys.ToDictionary(k => k, _ => Json(text))));

        foreach (var (label, options) in attempts)
        {
            var run = Execute(handler, options);

            if (run.Error is not null)
            {
                Assert.True(IsAcceptable(run.Error), $"{id} with [{label}] threw {run.Error.GetType().Name}: {run.Error.Message}");
                Assert.True(ReferenceEquals(run.Before, run.SessionAfter), $"{id} with [{label}] failed but changed the session");
            }
            Assert.True(run.BeforeFingerprint == run.BeforeFingerprintAfterRun, $"{id} with [{label}] modified its input in place");
        }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void EveryOperation_ConsultsItsWorkBudget(string id)
    {
        if (ConstantTimeOperations.Contains(id)) return;

        var starved = new OperationContext(CancellationToken.None, timeLimit: null, maxWorkUnits: 0);
        var run = Execute(Registry.TryGet(id)!, OperationExamples.Parse(id), starved);

        // If this fails, the operation never charged its context: give its TraceRecorder the context,
        // or call context.Checkpoint(...) inside its loops.
        var aborted = Assert.IsType<OperationAbortedException>(run.Error);
        Assert.Equal(OperationAbortReason.WorkBudgetExceeded, aborted.Reason);
    }
}