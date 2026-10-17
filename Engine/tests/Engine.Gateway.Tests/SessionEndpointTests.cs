using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class SessionEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateSession_ThenFillMatrix_StreamsStepsThenCompletes()
    {
        var client = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, "/session");
        var wsUri = new UriBuilder(uri) { Scheme = "ws" }.Uri;
        using var socket = await client.ConnectAsync(wsUri, CancellationToken.None);

        await SendAsync(socket, new CreateSessionMessage("matrix",
            JsonSerializer.SerializeToElement(new { rows = 2, columns = 2 })));
        var ready = Assert.IsType<SessionReadyMessage>(await ReceiveAsync(socket));
        Assert.NotEqual(Guid.Empty, ready.SessionId);

        await SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), "fill-even-odd", new()));

        for (int i = 0; i < 4; i++)
            Assert.IsType<StepEventMessage>(await ReceiveAsync(socket));

        Assert.IsType<OperationCompleteMessage>(await ReceiveAsync(socket));
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