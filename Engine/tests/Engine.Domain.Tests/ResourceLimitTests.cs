using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class ResourceLimitTests
{
    [Theory]
    [InlineData(1000, 1000)]       // 1,000,000 cells: plainly too big
    [InlineData(100_000, 100_000)] // product wraps to a positive int in 32-bit math
    public void Matrix_BeyondCellLimit_Throws(int rows, int columns)
    {
        Assert.Throws<ArgumentException>(() => new Matrix(rows, columns));
    }

    [Fact]
    public void Matrix_ExactlyAtLimit_Succeeds()
    {
        var matrix = new Matrix(500, 1000); // 500,000 cells
        Assert.Equal(500, matrix.Rows);
    }

    [Fact]
    public void Graph_NodeCountAboveLimit_Throws()
    {
        Assert.Throws<ArgumentException>(() => Graph.Empty(DatasetLimits.MaxCells + 1));
    }

    [Fact]
    public void Graph_NegativeNodeCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => Graph.Empty(-1));
    }

    [Fact]
    public void Graph_ZeroNodes_IsAllowed()
    {
        Assert.Equal(0, Graph.Empty(0).NodeCount);
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(100_000, 100_000)]
    public void GenerateMaze_BeyondCellLimit_Throws(int width, int height)
    {
        var op = new GenerateMazeOperation(width, height, seed: 1);
        Assert.Throws<ArgumentException>(() => op.Execute(Graph.Empty(1)));
    }
}