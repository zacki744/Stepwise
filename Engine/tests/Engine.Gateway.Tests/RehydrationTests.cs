using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class RehydrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Rehydrate_ReplaysLoggedOperations_ProducesSameCommittedState()
    {
        var client = factory.Server.CreateWebSocketClient();
        var wsUri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws", Path = "/session" }.Uri;

        // --- Original session: create, fill, disconnect ---
        using (var socket = await client.ConnectAsync(wsUri, CancellationToken.None))
        {
            await SendAsync(socket, new CreateSessionMessage("matrix",
                JsonSerializer.SerializeToElement(new { rows = 2, columns = 2 })));
            await ReceiveAsync(socket); // SessionReady

            await SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), "fill-even-odd", new()));
            for (int i = 0; i < 4; i++) await ReceiveAsync(socket); // 4 StepEvents
            await ReceiveAsync(socket); // OperationComplete

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }

        // --- Rehydrate on a fresh connection, using what the client would have persisted locally ---
        using var newSocket = await client.ConnectAsync(wsUri, CancellationToken.None);

        await SendAsync(newSocket, new RehydrateSessionMessage(
            DatasetType: "matrix",
            InitialPayload: JsonSerializer.SerializeToElement(new { rows = 2, columns = 2 }),
            OpLog: [new LoggedOperationDto("fill-even-odd", new Dictionary<string, JsonElement>())]));

        var ready = Assert.IsType<SessionReadyMessage>(await ReceiveAsync(newSocket));

        await SendAsync(newSocket, new ExecuteOperationMessage(Guid.NewGuid(), "read-cell",
            new Dictionary<string, JsonElement>
            {
                ["row"] = JsonSerializer.SerializeToElement(0),
                ["column"] = JsonSerializer.SerializeToElement(0)
            }));

        var complete = Assert.IsType<OperationCompleteMessage>(await ReceiveAsync(newSocket));
        Assert.Equal(2, complete.ResultSummary!.Value.GetInt32());
    }

    private static async Task SendAsync(WebSocket socket, ClientMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, ProtocolJson.Options));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<ServerMessage> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return JsonSerializer.Deserialize<ServerMessage>(Encoding.UTF8.GetString(buffer, 0, result.Count), ProtocolJson.Options)!;
    }
}