using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Session;
using Xunit;

namespace Engine.Session.Tests;

public class SessionManagerEvictIdleTests
{
    [Fact]
    public void EvictIdle_RemovesOnlySessionsPastThreshold()
    {
        var manager = new SessionManager();
        var stale = manager.Create(new Matrix(1, 1));
        Thread.Sleep(150);
        var fresh = manager.Create(new Matrix(1, 1));

        int evicted = manager.EvictIdle(TimeSpan.FromMilliseconds(75));

        Assert.Equal(1, evicted);
        Assert.Null(manager.TryGet(stale.Id));
        Assert.NotNull(manager.TryGet(fresh.Id));
    }

    [Fact]
    public async Task EvictIdle_SkipsSessionCurrentlyExecuting()
    {
        var manager = new SessionManager();
        var session = manager.Create(new Matrix(1, 1));
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var running = Task.Run(() => manager.Execute(session.Id, new BlockingOperation(started, release)));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        await Task.Delay(20); // let it become "older than zero" while still mid-operation

        int evicted = manager.EvictIdle(TimeSpan.Zero);

        Assert.Equal(0, evicted);
        Assert.NotNull(manager.TryGet(session.Id));

        release.Set();
        await running;
    }

    private sealed class BlockingOperation(ManualResetEventSlim started, ManualResetEventSlim release) : IOperation<Matrix>
    {
        public OperationMetadata Metadata { get; } = new("blocking", StateEffect.ReadOnly);

        public OperationExecutionResult Execute(Matrix input, OperationContext context)
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return new OperationExecutionResult(true, null, null, [], null);
        }
    }
}