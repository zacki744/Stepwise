using System.Net.WebSockets;
using System.Text;

namespace Engine.Gateway;

public sealed class MessageTooLargeException(int maxBytes)
    : Exception($"Message exceeds the {maxBytes:N0}-byte limit")
{
    public int MaxBytes { get; } = maxBytes;
}

public static class BoundedMessageReader
{
    /// <summary>
    /// Reads one complete text message, or null if the peer closed. Checks the limit as each
    /// frame arrives, so an endless fragmented message is rejected promptly instead of being
    /// buffered until memory runs out.
    /// </summary>
    public static async Task<string?> ReadAsync(WebSocket socket, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Echo the close frame so the client's CloseAsync can complete normally.
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", ct);
                return null;
            }

            if (stream.Length + result.Count > maxBytes)
                throw new MessageTooLargeException(maxBytes);

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}