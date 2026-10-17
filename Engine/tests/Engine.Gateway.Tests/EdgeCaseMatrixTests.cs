using System.Diagnostics;
using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Protocol;
using Engine.Session;
using Xunit;
using Xunit.Sdk;

namespace Engine.Gateway.Tests;

/// <summary>
/// Every operation against every adversarial dataset of its type (see <see cref="AdversarialDatasets"/>), using
/// the operation's example options. The rules, for each pair:
///  - it either succeeds or fails with an ArgumentException or a work-budget abort. Any other exception is
///    a bug: it would reach a client as "internal error" and be logged as unexpected.
///  - the dataset it was given is never changed
///  - two runs give identical results (rehydration replays operations and relies on this)
///  - whatever it returns can be turned into JSON for the wire, and the new dataset can be snapshotted
///  - a reply stays small
/// A failure names the operation and the dataset, so it can be reproduced in one line.
/// </summary>
public class EdgeCaseMatrixTests
{
    private static readonly OperationRegistry Registry = OperationCatalog.BuildDefault();

    public static IEnumerable<object[]> Cases() =>
        Registry.All.OrderBy(h => h.OperationId, StringComparer.Ordinal).SelectMany(h =>
            AdversarialDatasets.For(h.DatasetType).Select(d => new object[] { h.OperationId, d.Name }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Survives(string operationId, string datasetName)
    {
        var handler = Registry.TryGet(operationId)!;
        var dataset = AdversarialDatasets.For(handler.DatasetType).Single(d => d.Name == datasetName).Create();
        var before = OperationContractTests.Fingerprint(dataset);

        var first = EdgeCaseRunner.Run(handler, dataset, EdgeCaseRunner.NormalLimits());
        var second = EdgeCaseRunner.Run(handler, dataset, EdgeCaseRunner.NormalLimits());

        EdgeCaseRunner.AssertSound(first, $"{operationId} on '{datasetName}'");
        Assert.Equal(before, OperationContractTests.Fingerprint(dataset)); // the input must not change
        EdgeCaseRunner.AssertSame(first, second, $"{operationId} on '{datasetName}'");
    }

    [Fact]
    public void EveryDatasetType_HasAdversarialDatasets_AndTheNamesAreUnique()
    {
        foreach (var type in Registry.All.Select(h => h.DatasetType).Distinct())
        {
            var names = AdversarialDatasets.For(type).Select(d => d.Name).ToList();
            Assert.NotEmpty(names);
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
}

/// <summary>The same matrix at the dataset size limits. Slow and memory-hungry, so opt in: set STEPWISE_SLOW=1.</summary>
public class LargeDatasetEdgeCaseTests
{
    private static readonly OperationRegistry Registry = OperationCatalog.BuildDefault();

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<NamedDataset>>> Large = new(() =>
    {
        var matrix = new Matrix(1000, 500);
        var graph = (Graph)new GenerateRandomGraphOperation(100_000, 1_200_000, seed: 5, maxWeight: 9)
            .Execute(Graph.Empty(1), OperationContext.Unbounded).NewState!;
        var chain = Tree.FromNodes(0, Enumerable.Range(0, 400_000).Select(i => new TreeNodeData(
            i, i, -1, i + 1 < 400_000 ? i + 1 : -1)).ToList());

        return new Dictionary<string, IReadOnlyList<NamedDataset>>
        {
            ["matrix"] = [new("1000x500 open", () => matrix)],
            ["graph"] = [new("100,000 nodes, 2.4M edges", () => graph)],
            ["tree"] = [new("right chain of 400,000", () => chain)],
        };
    });

    public static IEnumerable<object[]> Cases() =>
        Registry.All.OrderBy(h => h.OperationId, StringComparer.Ordinal).Select(h => new object[] { h.OperationId });

    [SlowTheory]
    [MemberData(nameof(Cases))]
    public void Survives_AtTheSizeLimits(string operationId)
    {
        var handler = Registry.TryGet(operationId)!;
        var named = Large.Value[handler.DatasetType].Single();
        var dataset = named.Create();
        var before = OperationContractTests.Fingerprint(dataset);

        var timer = Stopwatch.StartNew();
        var run = EdgeCaseRunner.Run(handler, dataset, EdgeCaseRunner.LargeLimits());
        timer.Stop();

        EdgeCaseRunner.AssertSound(run, $"{operationId} on '{named.Name}'");
        Assert.Equal(before, OperationContractTests.Fingerprint(dataset));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"{operationId} took {timer.Elapsed.TotalSeconds:0.0}s even with a 3 s limit");
    }
}

/// <summary>A theory that only runs when STEPWISE_SLOW=1 is set.</summary>
public sealed class SlowTheoryAttribute : TheoryAttribute
{
    public SlowTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("STEPWISE_SLOW") != "1")
            Skip = "Slow: set STEPWISE_SLOW=1 to run (uses a lot of memory)";
    }
}

/// <summary>A fact that only runs when STEPWISE_SLOW=1 is set.</summary>
public sealed class SlowFactAttribute : FactAttribute
{
    public SlowFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("STEPWISE_SLOW") != "1")
            Skip = "Slow: set STEPWISE_SLOW=1 to run (uses a lot of memory)";
    }
}

internal static class EdgeCaseRunner
{
    public sealed record Limits(TimeSpan Time, long WorkUnits);

    public static Limits NormalLimits() => new(TimeSpan.FromSeconds(10), 20_000_000);
    public static Limits LargeLimits() => new(TimeSpan.FromSeconds(3), 3_000_000);

    public sealed class Result
    {
        public Exception? Error;
        public bool Succeeded;
        public string? FailureReason;
        public string? ValueJson;
        public Exception? ValueSerializationError;
        public string? NewStateFingerprint;
        public Exception? SnapshotError;
        public IDataset? NewState;
    }

    public static Result Run(IOperationHandler handler, IDataset dataset, Limits limits)
    {
        var options = OperationExamples.Parse(handler.OperationId).ToDictionary(p => p.Key, p => p.Value);
        var sessions = new SessionManager(maxSessions: 1);
        var session = sessions.Create(dataset);
        var result = new Result();

        OperationOutcome? outcome = null;
        try
        {
            outcome = handler.Execute(sessions, session.Id, options,
                new OperationContext(CancellationToken.None, limits.Time, limits.WorkUnits));
        }
        catch (Exception ex)
        {
            result.Error = ex;
            return result;
        }

        result.Succeeded = outcome.Succeeded;
        result.FailureReason = outcome.FailureReason;

        if (outcome.Value is not null)
        {
            try { result.ValueJson = JsonSerializer.Serialize(outcome.Value, ProtocolJson.Options); }
            catch (Exception ex) { result.ValueSerializationError = ex; }
        }

        if (outcome.Succeeded)
        {
            result.NewState = session.Dataset;
            result.NewStateFingerprint = OperationContractTests.Fingerprint(session.Dataset);
            try { DatasetSnapshots.TryCreate(session.Dataset); }
            catch (Exception ex) { result.SnapshotError = ex; }
        }
        return result;
    }

    public static bool IsExpectedFailure(Exception ex) => ex is ArgumentException or OperationAbortedException;

    public static void AssertSound(Result run, string what)
    {
        if (run.Error is not null && !IsExpectedFailure(run.Error))
            throw new XunitException($"{what}: unexpected {run.Error.GetType().Name}, which would reach the client as an internal error.\n{run.Error}");

        if (run.ValueSerializationError is not null)
            throw new XunitException($"{what}: the result cannot be serialized for the wire.\n{run.ValueSerializationError.Message}");

        if (run.SnapshotError is not null)
            throw new XunitException($"{what}: the resulting dataset cannot be snapshotted.\n{run.SnapshotError.Message}");

        if (run.ValueJson is { Length: > 4_000_000 })
            throw new XunitException($"{what}: the reply is {run.ValueJson.Length:N0} characters, which is not small");
    }

    public static void AssertSame(Result a, Result b, string what)
    {
        // A wall-clock abort depends on machine speed; everything else must repeat exactly.
        if (a.Error is OperationAbortedException { Reason: OperationAbortReason.TimeLimitExceeded }
            || b.Error is OperationAbortedException { Reason: OperationAbortReason.TimeLimitExceeded }) return;

        Assert.Equal(a.Error?.GetType(), b.Error?.GetType());
        Assert.Equal(a.Error?.Message, b.Error?.Message);
        Assert.True(a.Succeeded == b.Succeeded, $"{what}: succeeded once and not the other time");
        Assert.Equal(a.FailureReason, b.FailureReason);
        Assert.Equal(a.ValueJson, b.ValueJson);
        Assert.Equal(a.NewStateFingerprint, b.NewStateFingerprint);
    }
}
