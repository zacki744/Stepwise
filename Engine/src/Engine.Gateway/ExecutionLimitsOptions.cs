namespace Engine.Gateway;

public sealed class ExecutionLimitsOptions
{
    public int MaxConcurrentOperations { get; set; } = Math.Max(1, Environment.ProcessorCount);
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Wall-clock backstop. Non-deterministic by nature (depends on machine speed).</summary>
    public TimeSpan OperationTimeLimit { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Deterministic ceiling, same result on any machine. Starting value, not measured: tune it.</summary>
    public long MaxWorkUnits { get; set; } = 200_000_000;
}