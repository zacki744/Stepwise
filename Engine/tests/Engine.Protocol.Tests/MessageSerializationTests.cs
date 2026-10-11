using System.Text.Json;
using Engine.Protocol;
using Xunit;

namespace Engine.Protocol.Tests;

public class MessageSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    [Fact]
    public void ExecuteOperationMessage_RoundTripsThroughClientMessage()
    {
        ClientMessage original = new ExecuteOperationMessage(
            RequestId: Guid.NewGuid(),
            OperationId: "generate-maze",
            Options: new Dictionary<string, JsonElement>());

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ClientMessage>(json, Options);

        var result = Assert.IsType<ExecuteOperationMessage>(deserialized);
        Assert.Equal(((ExecuteOperationMessage)original).RequestId, result.RequestId);
        Assert.Equal("generate-maze", result.OperationId);
    }

    [Fact]
    public void ServerMessage_DeserializesToCorrectDerivedType()
    {
        ServerMessage original = new SessionReadyMessage(Guid.NewGuid());

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerMessage>(json, Options);

        Assert.IsType<SessionReadyMessage>(deserialized);
    }
}