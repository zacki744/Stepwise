using Engine.Session;

namespace Engine.Gateway;

public sealed class SessionLifecycleOptions
{
    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxSessions { get; set; } = SessionManager.DefaultMaxSessions;

}