using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

public class FillMatrixOperationTests
{
    [Fact]
    public void Execute_FillsCorrectPattern()
    {
        var op = new FillMatrixOperation();
        var input = new Matrix(2, 2);

        var result = op.Execute(input);

        Assert.True(result.Succeeded);
        var output = Assert.IsType<Matrix>(result.NewState);
        Assert.Equal(2, output[0, 0]); // (0+0) even
        Assert.Equal(1, output[0, 1]); // (0+1) odd
    }

    [Fact]
    public void Execute_DoesNotMutateInput()
    {
        var op = new FillMatrixOperation();
        var input = new Matrix(2, 2);

        op.Execute(input);

        Assert.Equal(0, input[0, 0]); // input untouched — proves working-copy isolation
    }

    [Fact]
    public void Execute_ProducesOneTraceEventPerCell()
    {
        var op = new FillMatrixOperation();
        var result = op.Execute(new Matrix(3, 4));

        Assert.Equal(12, result.Trace.Count);
    }
}