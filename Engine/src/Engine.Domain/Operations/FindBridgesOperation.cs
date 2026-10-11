using Engine.Domain.Datasets;
using Engine.Domain.Tracing;

namespace Engine.Domain.Operations;

public sealed record BridgeFoundPayload(int From, int To);
public sealed record BridgeEdge(int A, int B); // named fields, not a tuple: tuples don't survive JSON

/// <summary>
/// Bridges: connections whose removal disconnects the network. Iterative (see LowLinkAnalysis).
/// </summary>
public sealed class FindBridgesOperation : IOperation<Graph>
{
	public OperationMetadata Metadata { get; } = new("find-bridges", StateEffect.ProducesValue);

	public OperationExecutionResult Execute(Graph input, OperationContext context)
	{
		var analysis = new LowLinkAnalysis(input);
		var trace = new TraceRecorder(context);
		var bridges = new List<BridgeEdge>();

		foreach (var (a, b) in analysis.Bridges)
		{
			bridges.Add(new BridgeEdge(a, b));
			trace.Add(new DomainStepEvent("BridgeFound", $"{a}-{b} is a bridge", new BridgeFoundPayload(a, b)));
		}

		return new OperationExecutionResult(true, null, bridges, trace, null);
	}
}