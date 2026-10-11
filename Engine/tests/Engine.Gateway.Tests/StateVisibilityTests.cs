using System.Net.WebSockets;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class StateVisibilityTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<Guid> CreateAsync(WebSocket socket, string datasetType, object payload)
    {
        await GatewayTestHelpers.SendAsync(socket, new CreateSessionMessage(datasetType, Json(payload)));
        return Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket)).SessionId;
    }

    private static async Task<OperationCompleteMessage> ExecuteAsync(
        WebSocket socket, string operationId, Dictionary<string, JsonElement>? options = null)
    {
        await GatewayTestHelpers.SendAsync(socket,
            new ExecuteOperationMessage(Guid.NewGuid(), operationId, options ?? new()));
        while (true)
        {
            var message = await GatewayTestHelpers.ReceiveAsync(socket);
            if (message is OperationCompleteMessage complete) return complete;
            Assert.IsNotType<OperationFailedMessage>(message);
            Assert.IsNotType<ErrorMessage>(message);
        }
    }

    // ---------- getState ----------

    [Fact]
    public async Task GetState_ReturnsTheCurrentTree()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await CreateAsync(socket, "tree", new { values = new[] { 5, 3, 8 } });

        var requestId = Guid.NewGuid();
        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(requestId));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal(requestId, state.RequestId);
        Assert.Equal("tree", state.State.DatasetType);
        Assert.Equal("""{"root":0,"slotCount":3,"nodes":[[0,5,1,2],[1,3,-1,-1],[2,8,-1,-1]]}""",
            state.State.Payload.GetRawText());
    }

    [Fact]
    public async Task GetState_ReflectsEarlierOperations()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await CreateAsync(socket, "matrix", new { rows = 1, columns = 2 });
        await ExecuteAsync(socket, "set-cell", new()
        {
            ["row"] = Json(0), ["column"] = Json(1), ["value"] = Json(7)
        });

        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal("""{"rows":1,"columns":2,"cells":[0,7]}""", state.State.Payload.GetRawText());
    }

    [Fact]
    public async Task GetState_AfterRehydrate_ShowsTheReplayedState()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.SendAsync(socket, new RehydrateSessionMessage(
            "matrix", Json(new { rows = 1, columns = 2 }),
            [new LoggedOperationDto("set-cell", new() { ["row"] = Json(0), ["column"] = Json(0), ["value"] = Json(9) })]));
        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal("""{"rows":1,"columns":2,"cells":[9,0]}""", state.State.Payload.GetRawText());
    }

    [Fact]
    public async Task GetState_WithoutASession_IsANoSessionError()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));

        Assert.Equal("no-session", Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket)).Code);
    }

    [Fact]
    public async Task GetState_ForAnEvictedSession_FailsWithSessionExpired()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var sessions = (Engine.Session.SessionManager)factory.Services.GetService(typeof(Engine.Session.SessionManager))!;
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        var id = await CreateAsync(socket, "tree", new { });
        sessions.Evict(id);

        var requestId = Guid.NewGuid();
        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(requestId));
        var failed = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));

        Assert.Equal(requestId, failed.RequestId);
        Assert.StartsWith("session-expired", failed.Reason);
    }

    // ---------- finalState on operationComplete ----------

    [Fact]
    public async Task SmallStateChange_AttachesTheNewStateWithoutAnyCoalescing()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await CreateAsync(socket, "matrix", new { rows = 1, columns = 2 });

        var complete = await ExecuteAsync(socket, "set-cell", new()
        {
            ["row"] = Json(0), ["column"] = Json(1), ["value"] = Json(7)
        });

        Assert.NotNull(complete.FinalState);
        Assert.Equal("""{"rows":1,"columns":2,"cells":[0,7]}""", complete.FinalState!.Payload.GetRawText());
    }

    [Fact]
    public async Task TreeInsert_AttachesTheNewTree()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await CreateAsync(socket, "tree", new { values = new[] { 5 } });

        var complete = await ExecuteAsync(socket, "insert-node", new() { ["value"] = Json(9) });

        Assert.Equal("""{"root":0,"slotCount":2,"nodes":[[0,5,-1,1],[1,9,-1,-1]]}""",
            complete.FinalState!.Payload.GetRawText());
    }

    [Fact]
    public async Task ReadOnlyOperation_AttachesNoState()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await CreateAsync(socket, "tree", new { values = new[] { 5 } });

        var complete = await ExecuteAsync(socket, "tree-stats");

        Assert.Null(complete.FinalState);
    }

    [Fact]
    public async Task LargeDataset_IsNotAttachedEveryTime_ButGetStateStillReturnsIt()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await CreateAsync(socket, "matrix", new { rows = 200, columns = 200 }); // 40,000 cells: over the inline limit

        var complete = await ExecuteAsync(socket, "set-cell", new()
        {
            ["row"] = Json(0), ["column"] = Json(0), ["value"] = Json(1)
        });
        Assert.Null(complete.FinalState);

        await GatewayTestHelpers.SendAsync(socket, new GetStateMessage(Guid.NewGuid()));
        var state = Assert.IsType<StateMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(40_000, state.State.Payload.GetProperty("cells").GetArrayLength());
    }
}
