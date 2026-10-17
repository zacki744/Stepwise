using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class TextImportEndpointTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task CreateSession_FromEdgeListText_ThenGetState_ReturnsTheGraph()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendAsync(socket,
            new CreateSessionMessage("graph", Json(new { text = "# triangle\n0 1\n1 2\n2 0 5" })));
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal("graph", state.State.DatasetType);
        Assert.Equal(3, state.State.Payload.GetProperty("nodeCount").GetInt32());
        Assert.Equal(6, state.State.Payload.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public async Task CreateSession_FromGridText_ThenGetState_ReturnsTheMatrix()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendAsync(socket, new CreateSessionMessage("matrix", Json(new { text = "1 2\n3 4" })));
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal("""{"rows":2,"columns":2,"cells":[1,2,3,4]}""", state.State.Payload.GetRawText());
    }

    [Fact]
    public async Task CreateSession_WithABadLine_ReportsTheLine_AndTheConnectionStaysUsable()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendAsync(socket,
            new CreateSessionMessage("graph", Json(new { text = "0 1\n1 two" })));
        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal("invalid-create-session", error.Code);
        Assert.StartsWith("line 2:", error.Message);
        Assert.NotEqual(Guid.Empty, await GatewayTestHelpers.CreateMatrixSessionAsync(socket));
    }

    [Fact]
    public async Task Rehydrate_WithATextPayload_ReplaysOnTopOfIt()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendAsync(socket, new RehydrateSessionMessage("tree", Json(new { text = "5 3" }),
            [new LoggedOperationDto("insert-node", new() { ["value"] = Json(8) })]));
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal(3, state.State.Payload.GetProperty("nodes").GetArrayLength());
    }
}
