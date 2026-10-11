using System.Net.WebSockets;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class MalformedMessageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // A cheap round trip that only works if the connection and session are both still healthy.
    private static async Task AssertStillUsable(WebSocket socket)
    {
        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), "read-cell",
            new Dictionary<string, JsonElement>
            {
                ["row"] = JsonSerializer.SerializeToElement(0),
                ["column"] = JsonSerializer.SerializeToElement(0)
            }));

        var done = Assert.IsType<OperationCompleteMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(0, done.ResultSummary!.Value.GetInt32());
    }

    private async Task<WebSocket> ConnectWithMatrixSession()
    {
        var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);
        return socket;
    }

    [Fact]
    public async Task MissingOperationId_FailsThatRequest_AndTheConnectionSurvives()
    {
        using var socket = await ConnectWithMatrixSession();
        var requestId = Guid.NewGuid();

        await GatewayTestHelpers.SendRawAsync(socket, $$"""{"type":"executeOperation","requestId":"{{requestId}}"}""");

        var failed = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal(requestId, failed.RequestId);
        Assert.Contains("operationId", failed.Reason);
        await AssertStillUsable(socket);
    }

    [Fact]
    public async Task MissingOptions_IsReportedAsInvalidOptions_NotAnInternalError()
    {
        using var socket = await ConnectWithMatrixSession();

        await GatewayTestHelpers.SendRawAsync(socket,
            $$"""{"type":"executeOperation","requestId":"{{Guid.NewGuid()}}","operationId":"set-cell"}""");

        var failed = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.StartsWith("Invalid options", failed.Reason);
        await AssertStillUsable(socket);
    }

    [Fact]
    public async Task AnOperationForTheWrongDatasetType_SaysSo()
    {
        using var socket = await ConnectWithMatrixSession();

        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), "insert-node",
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(1) }));

        var failed = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.StartsWith("wrong-dataset", failed.Reason);
        await AssertStillUsable(socket);
    }

    [Fact]
    public async Task ANullMessage_GetsABadRequestReply()
    {
        using var socket = await ConnectWithMatrixSession();

        await GatewayTestHelpers.SendRawAsync(socket, "null");

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("bad-request", error.Code);
        await AssertStillUsable(socket);
    }

    [Fact]
    public async Task ACreateSessionWithAShortEdge_IsRejectedAsBadInput()
    {
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendRawAsync(socket,
            """{"type":"createSession","datasetType":"graph","initialPayload":{"nodeCount":3,"edges":[[0]]}}""");

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("invalid-create-session", error.Code);

        await GatewayTestHelpers.CreateMatrixSessionAsync(socket); // and the connection still works
    }

    [Fact]
    public async Task ACreateSessionWithoutADatasetType_IsRejectedAsBadInput()
    {
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendRawAsync(socket, """{"type":"createSession"}""");

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("invalid-create-session", error.Code);
    }

    [Fact]
    public async Task ARehydrateWithoutAnOpLog_JustStartsFromTheInitialPayload()
    {
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendRawAsync(socket,
            """{"type":"rehydrateSession","datasetType":"matrix","initialPayload":{"rows":2,"columns":2}}""");

        Assert.IsType<SessionReadyMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        await AssertStillUsable(socket);
    }
}