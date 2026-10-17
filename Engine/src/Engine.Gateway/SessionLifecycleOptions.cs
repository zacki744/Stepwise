using Engine.Session;

namespace Engine.Gateway;

public sealed class SessionLifecycleOptions
{
    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxSessions { get; set; } = SessionManager.DefaultMaxSessions;

    /// <summary>Estimated bytes all sessions' datasets may use together (default 768 MiB; keep it well under the container memory limit).</summary>
    public long MaxTotalDatasetBytes { get; set; } = SessionManager.DefaultMaxTotalDatasetBytes;

}