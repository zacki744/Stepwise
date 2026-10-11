using System.Text.Json;
using Engine.Domain.Datasets;
using Engine.Domain.Operations;
using Engine.Protocol;
using Xunit;

namespace Engine.Gateway.Tests;

public class OperationValueWireTests
{
    [Fact]
    public void FindBridges_Value_SerializesWithNamedFields()
    {
        var graph = new Graph(2, [(0, 1, 1.0), (1, 0, 1.0)]);
        var value = new FindBridgesOperation().Execute(graph).Value;

        Assert.Equal("""[{"a":0,"b":1}]""", JsonSerializer.Serialize(value, ProtocolJson.Options));
    }

    [Fact]
    public void MatrixToGraph_Value_SerializesWithNamedFields()
    {
        var value = new MatrixToGraphOperation().Execute(new Matrix(1, 2)).Value;

        Assert.Equal("""[{"row":0,"column":0},{"row":0,"column":1}]""",
            JsonSerializer.Serialize(value, ProtocolJson.Options));
    }
}