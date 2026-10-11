using System.Net.WebSockets;
using Xunit;

namespace Engine.Gateway.Tests;

public class BoundedMessageReaderTests
{
    [Fact]
    public async Task ReadAsync_MessageWithinLimit_ReturnsItsText()
    {
        var socket = new ScriptedWebSocket((60, false), (40, true));

        var text = await BoundedMessageReader.ReadAsync(socket, maxBytes: 100, CancellationToken.None);

        Assert.Equal(100, text!.Length);
    }

    [Fact]
    public async Task ReadAsync_MessageOneByteOverLimit_Throws()
    {
        var socket = new ScriptedWebSocket((60, false), (41, true));

        await Assert.ThrowsAsync<MessageTooLargeException>(() =>
            BoundedMessageReader.ReadAsync(socket, maxBytes: 100, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_EndlessMessage_StopsReadingAtTheLimit()
    {
        var socket = new ScriptedWebSocket(Enumerable.Repeat((8192, false), 1000).ToArray());

        await Assert.ThrowsAsync<MessageTooLargeException>(() =>
            BoundedMessageReader.ReadAsync(socket, maxBytes: 20_000, CancellationToken.None));

        Assert.Equal(3, socket.ChunksRead); // 8192, 16384, then 24576 > 20000 and it gives up
    }

    // Hands back scripted frames of 'a' bytes so the reader's logic can be tested deterministically.
    private sealed class ScriptedWebSocket(params (int Count, bool End)[] chunks) : WebSocket
    {
        private int _next;
        public int ChunksRead => _next;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort() { }
        public override void Dispose() { }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            var (count, end) = chunks[_next++];
            Array.Fill(buffer.Array!, (byte)'a', buffer.Offset, count);
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, end));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}