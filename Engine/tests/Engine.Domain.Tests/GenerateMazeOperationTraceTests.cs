using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Xunit;
using static Engine.Domain.Operations.GenerateMazeOperation;

namespace Engine.Domain.Tests;

public class GenerateMazeOperationTraceTests
{
    [Fact]
    public void Execute_EveryPassageCarvedStep_HasStructuredPayload()
    {
        var result = new GenerateMazeOperation(width: 4, height: 4, seed: 1).Execute(Graph.Empty(16));

        foreach (var step in result.Trace)
        {
            var payload = Assert.IsType<PassageCarvedPayload>(step.Payload);
            Assert.InRange(payload.From, 0, 15);
            Assert.InRange(payload.To, 0, 15);
        }
    }
}