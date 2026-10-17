using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class OperationContextTests
{
    private static Matrix FullGrid(int size)
    {
        var grid = new Matrix(size, size);
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                grid[r, c] = 9;
        return grid;
    }

    [Fact]
    public void Checkpoint_PastWorkBudget_Throws()
    {
        var context = new OperationContext(CancellationToken.None, timeLimit: null, maxWorkUnits: 10);
        for (int i = 0; i < 10; i++) context.Checkpoint();

        var ex = Assert.Throws<OperationAbortedException>(() => context.Checkpoint());

        Assert.Equal(OperationAbortReason.WorkBudgetExceeded, ex.Reason);
    }

    [Fact]
    public void Checkpoint_AfterCancellation_ThrowsOnceTheClockIsChecked()
    {
        using var cts = new CancellationTokenSource();
        var context = new OperationContext(cts.Token, timeLimit: null, maxWorkUnits: long.MaxValue);
        cts.Cancel();

        var ex = Assert.Throws<OperationAbortedException>(() =>
        {
            for (int i = 0; i < 5_000; i++) context.Checkpoint(); // observed every ~1,024 units
        });

        Assert.Equal(OperationAbortReason.Cancelled, ex.Reason);
    }

    [Fact]
    public void Checkpoint_PastTimeLimit_Throws()
    {
        var context = new OperationContext(CancellationToken.None, TimeSpan.Zero, long.MaxValue);

        var ex = Assert.Throws<OperationAbortedException>(() =>
        {
            for (int i = 0; i < 5_000; i++) context.Checkpoint();
        });

        Assert.Equal(OperationAbortReason.TimeLimitExceeded, ex.Reason);
    }

    [Fact]
    public void Unbounded_NeverAborts()
    {
        var context = OperationContext.Unbounded;
        for (int i = 0; i < 100_000; i++) context.Checkpoint();
    }

    [Fact]
    public void PlaceItems_ExceedingWorkBudget_Aborts()
    {
        // No free cell: a 1x1 item must scan all 10,000 positions.
        var op = new PlaceItemsOperation([new BinItem(Id: 1, Width: 1, Height: 1)]);
        var context = new OperationContext(CancellationToken.None, null, maxWorkUnits: 500);

        var ex = Assert.Throws<OperationAbortedException>(() => op.Execute(FullGrid(100), context));

        Assert.Equal(OperationAbortReason.WorkBudgetExceeded, ex.Reason);
    }

    [Fact]
    public void TracedOperation_IsInterruptibleThroughItsTrace()
    {
        // A 50x50 maze records 2,499 steps; a budget of 100 must stop it mid-way.
        var context = new OperationContext(CancellationToken.None, null, maxWorkUnits: 100);

        Assert.Throws<OperationAbortedException>(() =>
            new GenerateMazeOperation(50, 50, seed: 1).Execute(Graph.Empty(1), context));
    }

    [Fact]
    public void WorkUnits_CountsEveryCheckpointUnit()
    {
        var context = new OperationContext(CancellationToken.None, null, maxWorkUnits: 1_000);

        context.Checkpoint();
        context.Checkpoint(41);

        Assert.Equal(42, context.WorkUnits);
    }

    [Fact]
    public void WorkUnits_IncludeEveryTracedStep()
    {
        var context = new OperationContext(CancellationToken.None, null, maxWorkUnits: long.MaxValue);

        new GenerateMazeOperation(5, 5, seed: 1).Execute(Graph.Empty(1), context);

        Assert.True(context.WorkUnits >= 24, $"a 5x5 maze records 24 steps but charged {context.WorkUnits}");
    }
}
