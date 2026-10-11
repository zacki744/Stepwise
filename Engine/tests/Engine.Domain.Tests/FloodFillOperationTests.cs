using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class FloodFillOperationTests
{
    [Fact]
    public void Execute_FillsEntireConnectedRegion()
    {
        var grid = new Matrix(3, 3); // all zeros

        var result = new FloodFillOperation(1, 1, 5).Execute(grid);

        var filled = Assert.IsType<Matrix>(result.NewState);
        Assert.Equal(5, filled[0, 0]);
        Assert.Equal(5, filled[2, 2]);
    }

    [Fact]
    public void Execute_RespectsBoundary_DoesNotCrossDifferentValue()
    {
        var grid = new Matrix(1, 3);
        grid[0, 1] = 9; // a wall splitting the row

        var result = new FloodFillOperation(0, 0, 5).Execute(grid);

        var filled = Assert.IsType<Matrix>(result.NewState);
        Assert.Equal(5, filled[0, 0]);
        Assert.Equal(9, filled[0, 1]); // untouched — different original value
        Assert.Equal(0, filled[0, 2]); // unreachable — never visited
    }

    [Fact]
    public void Execute_NewValueEqualsOld_IsNoOp()
    {
        var grid = new Matrix(2, 2);

        var result = new FloodFillOperation(0, 0, 0).Execute(grid);

        Assert.Empty(result.Trace);
    }

    [Fact]
    public void Execute_DoesNotMutateInputGrid()
    {
        var grid = new Matrix(2, 2);

        new FloodFillOperation(0, 0, 9).Execute(grid);

        Assert.Equal(0, grid[0, 0]); // same isolation guarantee as every other operation
    }
}