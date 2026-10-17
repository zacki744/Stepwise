using System.Collections.Concurrent;
using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Operations;
using Engine.Session;
using Xunit;
using Xunit.Sdk;

namespace Engine.Gateway.Tests;

/// <summary>
/// Long random sequences of operations on many sessions at once, on the shared session manager. After every
/// step, and at the end, the invariants a real deployment depends on must hold:
///  - only expected failures (ArgumentException, work-budget abort) ever occur
///  - a failed or aborted step leaves the dataset exactly as it was
///  - the sessions' memory accounting adds up, and returns to zero once they are evicted
///  - replaying a session's successful operations on a fresh session reaches the identical dataset
///    (this is what rehydration after a reconnect relies on)
/// Options are mostly each operation's example, sometimes with numbers swapped for random small ones so the
/// failure paths (node out of range, bad sizes) are exercised as often as the success paths.
/// </summary>
public class SoakTests
{
    private static readonly OperationRegistry Registry = OperationCatalog.BuildDefault();

    private sealed record Step(string OperationId, Dictionary<string, JsonElement> Options);

    private sealed record SessionRun(Guid SessionId, IDataset Initial, List<Step> Log, string FinalFingerprint);

    private static IEnumerable<NamedDataset> StartingPoints() =>
        new[] { "matrix", "graph", "tree" }
            .SelectMany(AdversarialDatasets.For)
            .Where(d => !d.Name.Contains("50,000")); // the long chains are covered by the edge-case matrix

    private static OperationContext Context() => new(CancellationToken.None, TimeSpan.FromSeconds(5), 5_000_000);

    private static Dictionary<string, JsonElement> PickOptions(IOperationHandler handler, Random random)
    {
        var options = OperationExamples.Parse(handler.OperationId).ToDictionary(p => p.Key, p => p.Value);
        if (random.Next(4) != 0) return options; // three times in four: the valid example as it is

        foreach (var option in OperationDescriptors.TryGet(handler.OperationId)!.Options)
        {
            if (option.Kind is OptionKinds.Int or OptionKinds.Row or OptionKinds.Column or OptionKinds.Node or OptionKinds.Seed
                && random.Next(2) == 0)
            {
                options[option.Name] = JsonSerializer.SerializeToElement(random.Next(-2, 13));
            }
        }
        return options;
    }

    private static SessionRun RunSession(SessionManager sessions, IDataset initial, int seed, int steps, ConcurrentBag<string> problems)
    {
        var random = new Random(seed);
        var session = sessions.Create(initial);
        var log = new List<Step>();

        for (int i = 0; i < steps; i++)
        {
            var type = session.Dataset.GetType().Name.ToLowerInvariant();
            var candidates = Registry.All.Where(h => h.DatasetType == type).OrderBy(h => h.OperationId, StringComparer.Ordinal).ToList();
            var handler = candidates[random.Next(candidates.Count)];
            var options = PickOptions(handler, random);
            var before = OperationContractTests.Fingerprint(session.Dataset);
            var where = $"seed {seed}, step {i}, {handler.OperationId} {JsonSerializer.Serialize(options)}";

            bool committed = false;
            try
            {
                var outcome = handler.Execute(sessions, session.Id, options, Context());
                committed = outcome.Succeeded;
            }
            catch (Exception ex) when (EdgeCaseRunner.IsExpectedFailure(ex))
            {
            }
            catch (Exception ex)
            {
                problems.Add($"{where}: unexpected {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (committed) log.Add(new Step(handler.OperationId, options));
            else if (OperationContractTests.Fingerprint(session.Dataset) != before)
                problems.Add($"{where}: the step failed but the dataset changed");
        }

        return new SessionRun(session.Id, initial, log, OperationContractTests.Fingerprint(session.Dataset));
    }

    // Helper so the handler list stays the single place operations are looked up.
    private static void Replay(SessionManager sessions, Guid sessionId, Step step)
    {
        var handler = Registry.TryGet(step.OperationId)!;
        handler.Execute(sessions, sessionId, step.Options, Context());
    }

    [Fact]
    public void RandomOperationSequences_KeepEveryInvariant_AcrossConcurrentSessions() => Soak(sessionCount: 60, steps: 30);

    [SlowFact]
    public void RandomOperationSequences_LongRun() => Soak(sessionCount: 400, steps: 80);

    private static void Soak(int sessionCount, int steps)
    {
        var starts = StartingPoints().ToList();
        var sessions = new SessionManager(maxSessions: sessionCount + 10);
        var problems = new ConcurrentBag<string>();
        var runs = new ConcurrentBag<SessionRun>();

        Parallel.For(0, sessionCount, new ParallelOptions { MaxDegreeOfParallelism = 4 }, seed =>
        {
            var start = starts[seed % starts.Count];
            runs.Add(RunSession(sessions, start.Create(), seed, steps, problems));
        });

        // 1. nothing unexpected, and failures changed nothing
        Assert.True(problems.IsEmpty, string.Join("\n", problems.Take(10)));

        // 2. the memory accounting is exactly the sum of what the sessions hold
        long held = runs.Sum(r => sessions.TryGet(r.SessionId)!.Dataset.EstimatedBytes);
        Assert.Equal(held, sessions.TotalDatasetBytes);

        // 3. replaying the successful operations reproduces each session's final dataset
        var replayProblems = new List<string>();
        foreach (var run in runs)
        {
            var fresh = new SessionManager(maxSessions: 1);
            var replay = fresh.Create(run.Initial);
            try
            {
                foreach (var step in run.Log) Replay(fresh, replay.Id, step);
            }
            catch (Exception ex)
            {
                replayProblems.Add($"replay of session {run.SessionId} threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            if (OperationContractTests.Fingerprint(replay.Dataset) != run.FinalFingerprint)
                replayProblems.Add($"replay of session {run.SessionId} ({run.Log.Count} steps) ended in a different dataset");
        }
        if (replayProblems.Count > 0) throw new XunitException(string.Join("\n", replayProblems.Take(10)));

        // 4. evicting everything gives all the memory back
        foreach (var run in runs) sessions.Evict(run.SessionId);
        Assert.Equal(0, sessions.TotalDatasetBytes);
        Assert.Equal(0, sessions.ActiveSessionCount);
    }
}
