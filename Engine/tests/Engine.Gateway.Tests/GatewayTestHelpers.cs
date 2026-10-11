using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

internal static class GatewayTestHelpers
{
    public static async Task<WebSocket> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        var wsUri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws", Path = "/session" }.Uri;
        return await client.ConnectAsync(wsUri, CancellationToken.None);
    }

    public static async Task SendAsync(WebSocket socket, ClientMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, ProtocolJson.Options));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public static async Task<ServerMessage> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return JsonSerializer.Deserialize<ServerMessage>(
            Encoding.UTF8.GetString(stream.ToArray()), ProtocolJson.Options)!;
    }

    public static Task SendCreateMatrixAsync(WebSocket socket, int rows, int columns) =>
        SendAsync(socket, new CreateSessionMessage("matrix",
            JsonSerializer.SerializeToElement(new { rows, columns })));

    public static async Task<Guid> CreateMatrixSessionAsync(WebSocket socket)
    {
        await SendCreateMatrixAsync(socket, 2, 2);
        return Assert.IsType<SessionReadyMessage>(await ReceiveAsync(socket)).SessionId;
    }
    public static async Task SendRawAsync(WebSocket socket, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}