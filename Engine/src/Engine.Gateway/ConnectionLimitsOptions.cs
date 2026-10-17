namespace Engine.Gateway;

public sealed class ConnectionLimitsOptions
{
    /// <summary>Largest single client message accepted. 1 MiB is roughly 50k graph edges of JSON.</summary>
    public int MaxMessageBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Largest createSession/rehydrateSession payload accepted when it is sent in pieces (uploadChunk
    /// messages). Each piece is still bound by MaxMessageBytes; this caps the reassembled whole.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 16L * 1024 * 1024;

    /// <summary>Most chunks one upload may be split into. Stops a flood of tiny pieces.</summary>
    public int MaxUploadChunks { get; set; } = 4096;

    /// <summary>Buffered upload bytes allowed across ALL connections at once.</summary>
    public long MaxInFlightUploadBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Simultaneous connections allowed from one client address (each owns at most one session). 0 = no limit.</summary>
    public int MaxConnectionsPerIp { get; set; } = 10;

    /// <summary>A connection that has no session yet must send something within this long, or it is closed. Stops idle sockets from holding per-address slots.</summary>
    public TimeSpan PreSessionTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A connection that owns a session may stay silent this long before it is closed (matches the session idle sweep). Zero or less = no limit.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Once a message has started arriving, the rest must follow within this long. Zero or less = no limit.</summary>
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the server pings an otherwise quiet connection.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the pong; a half-open connection (peer vanished without closing) is aborted after this. Zero = never.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>An upload must finish within this long of its first chunk, or the connection is closed.</summary>
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromMinutes(2);
}