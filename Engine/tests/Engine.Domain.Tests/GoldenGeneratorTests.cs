using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;

namespace Engine.Domain.Tests;

/// <summary>
/// Pins what a seed produces. Saved operation logs replay these generators, so a change in output
/// (a new .NET version, a refactor of the draw order) must fail loudly here instead of silently
/// corrupting restored sessions. Values come from an independent port of the algorithm.
/// </summary>
public class GoldenGeneratorTests
{
    [Fact]
    public void GenerateMaze_4x4_Seed42_CarvesTheExpectedPassages()
    {
        var result = new GenerateMazeOperation(4, 4, 42).Execute(Graph.Empty(16));

        var passages = result.Trace
            .Select(step => Assert.IsType<GenerateMazeOperation.PassageCarvedPayload>(step.Payload))
            .Select(p => (p.From, p.To))
            .ToArray();

        Assert.Equal(new[]
        {
            (0, 1), (1, 2), (2, 6), (6, 10), (10, 9), (9, 5), (5, 4), (4, 8),
            (8, 12), (12, 13), (13, 14), (14, 15), (15, 11), (11, 7), (7, 3)
        }, passages);
    }

    [Fact]
    public void RandomFill_2x3_Seed7_Max10_ProducesTheExpectedCells()
    {
        var result = new RandomFillOperation(seed: 7, maxValue: 10).Execute(new Matrix(2, 3));
        var matrix = (Matrix)result.NewState!;

        var cells = new List<int>();
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
                cells.Add(matrix[r, c]);

        Assert.Equal(new[] { 7, 4, 6, 3, 4, 5 }, cells);
    }
}
