namespace Engine.Gateway;

public sealed class ConnectionLimitsOptions
{
    /// <summary>Largest single client message accepted. 1 MiB is roughly 50k graph edges of JSON.</summary>
    public int MaxMessageBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Largest createSession/rehydrateSession payload accepted when it is sent in pieces (uploadChunk
    /// messages). Each piece is still bound by MaxMessageBytes; this caps the reassembled whole.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Most chunks one upload may be split into. Stops a flood of tiny pieces.</summary>
    public int MaxUploadChunks { get; set; } = 4096;

    /// <summary>Buffered upload bytes allowed across ALL connections at once.</summary>
    public long MaxInFlightUploadBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>An upload must finish within this long of its first chunk, or the connection is closed.</summary>
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromMinutes(2);
}