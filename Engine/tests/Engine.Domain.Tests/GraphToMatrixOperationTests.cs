using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Tests.GraphTestData;

namespace Engine.Domain.Tests;

public class GraphToMatrixOperationTests
{
    private static Matrix ToMatrix(Graph graph) =>
        Assert.IsType<Matrix>(new GraphToMatrixOperation().Execute(graph).NewState);

    [Fact]
    public void Directed_GivesEdgeMultiplicity()
    {
        var matrix = ToMatrix(Directed(3, (0, 1), (1, 2), (1, 2)));

        Assert.Equal(new[] { new[] { 0, 1, 0 }, new[] { 0, 0, 2 }, new[] { 0, 0, 0 } }, MatrixTestData.Rows(matrix));
    }

    [Fact]
    public void Undirected_GivesASymmetricMatrix()
    {
        var matrix = ToMatrix(Undirected(3, (0, 1), (1, 2)));

        Assert.Equal(new[] { new[] { 0, 1, 0 }, new[] { 1, 0, 1 }, new[] { 0, 1, 0 } }, MatrixTestData.Rows(matrix));
    }

    [Fact]
    public void RowSum_IsTheNodesOutDegree()
    {
        var matrix = ToMatrix(Undirected(3, (0, 1), (1, 2))); // node 1 has two neighbours

        var row = Assert.IsType<AggregateResult>(new AggregateMatrixOperation("row", 1).Execute(matrix).Value);

        Assert.Equal(2, row.Sum);
    }

    [Fact]
    public void ReversingTheEdges_GivesTheTransposedMatrix()
    {
        var graph = Directed(4, (0, 1), (0, 2), (2, 3), (3, 1));

        var reversed = Assert.IsType<Graph>(new ReverseEdgesOperation().Execute(graph).NewState);
        var transposed = Assert.IsType<Matrix>(new TransposeMatrixOperation().Execute(ToMatrix(graph)).NewState);

        Assert.Equal(MatrixTestData.Rows(transposed), MatrixTestData.Rows(ToMatrix(reversed)));
    }

    [Fact]
    public void LargestAllowedGraph_Converts()
    {
        Assert.Equal(707, ToMatrix(Graph.Empty(707)).Rows); // 707 * 707 = 499,849 cells
    }

    [Theory]
    [InlineData(0)]    // nothing to put in a matrix
    [InlineData(1_000)] // 1,000,000 cells: over the limit
    public void Unrepresentable_IsRejected(int nodeCount)
    {
        Assert.Throws<ArgumentException>(() => new GraphToMatrixOperation().Execute(Graph.Empty(nodeCount)));
    }
}