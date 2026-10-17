using System.Text.Json;
using Engine.Domain.Operations;
using Engine.Protocol;
using Engine.Session;
using Xunit;

namespace Engine.Gateway.Tests;

/// <summary>
/// The declared outputs (what an operation returns, which steps it narrates, how to draw them) must match
/// what the engine really does. Every operation is run on its example and each value and step it produces is
/// checked against the declaration, so the descriptors a UI is built from cannot drift from reality.
/// </summary>
public class OperationOutputTests
{
    private static readonly OperationRegistry Registry = OperationCatalog.BuildDefault();

    public static IEnumerable<object[]> AllOperationIds() =>
        OperationDefinitions.All.Select(d => new object[] { d.Descriptor.Id });

    private static readonly Dictionary<string, string[]> RolesByVisual = new()
    {
        [StepVisuals.None] = [],
        [StepVisuals.HighlightNode] = ["node"],
        [StepVisuals.HighlightNodes] = ["nodes"],
        [StepVisuals.HighlightPath] = ["path"],
        [StepVisuals.HighlightEdge] = ["from", "to"],
        [StepVisuals.HighlightCell] = ["column", "row"],
        [StepVisuals.HighlightSlot] = ["slot"],
    };

    [Fact]
    public void EveryOperationDeclaresOutput_AndNothingElseDoes()
    {
        Assert.Equal(
            OperationDefinitions.All.Select(d => d.Descriptor.Id).OrderBy(i => i, StringComparer.Ordinal),
            OperationOutputs.ById.Keys.OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public void ReturnsAreDeclared_ExactlyForOperationsThatProduceAValue()
    {
        foreach (var definition in OperationDefinitions.All)
        {
            bool producesValue = definition.Effect is StateEffect.ProducesValue or StateEffect.ReplacesStateAndProducesValue;
            Assert.True(producesValue == (definition.Output.Returns is not null),
                $"{definition.Descriptor.Id}: effect {definition.Effect} but returns is {(definition.Output.Returns is null ? "missing" : "declared")}");
        }
    }

    [Fact]
    public void EveryStepAndReturnIsDescribed_AndStepTypesAreUnique()
    {
        foreach (var definition in OperationDefinitions.All)
        {
            var id = definition.Descriptor.Id;
            if (definition.Output.Returns is { } returns)
                Assert.False(string.IsNullOrWhiteSpace(returns.Description), $"{id}: returns has no description");

            var types = definition.Output.Steps.Select(s => s.Type).ToList();
            Assert.Equal(types.Count, types.Distinct().Count());
            foreach (var step in definition.Output.Steps)
                Assert.False(string.IsNullOrWhiteSpace(step.Description), $"{id}/{step.Type}: no description");
        }
    }

    [Fact]
    public void Visuals_UseTheFixedVocabulary_AndBindToRealPayloadFields()
    {
        foreach (var definition in OperationDefinitions.All)
            foreach (var step in definition.Output.Steps)
            {
                var where = $"{definition.Descriptor.Id}/{step.Type}";
                Assert.True(RolesByVisual.ContainsKey(step.Visual.Kind), $"{where}: unknown visual '{step.Visual.Kind}'");
                Assert.Equal(RolesByVisual[step.Visual.Kind], step.Visual.Bind.Keys.OrderBy(k => k, StringComparer.Ordinal));

                foreach (var (role, field) in step.Visual.Bind)
                {
                    var shape = step.Payload.SingleOrDefault(f => f.Name == field);
                    Assert.True(shape is not null, $"{where}: visual binds {role} to '{field}', which the payload does not have");
                    string expected = role is "nodes" or "path" ? "list" : "int";
                    Assert.True(shape!.Type == expected, $"{where}: {role} -> {field} should be {expected} but is {shape.Type}");
                }
            }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void RealValueAndSteps_MatchTheirDeclaration(string id)
    {
        var definition = OperationDefinitions.TryGet(id)!;
        var handler = Registry.TryGet(id)!;

        var sessions = new SessionManager(maxSessions: 1);
        var session = sessions.Create(OperationContractTests.NewDataset(handler.DatasetType));
        var outcome = handler.Execute(sessions, session.Id, OperationExamples.Parse(id),
            new OperationContext(CancellationToken.None, TimeSpan.FromSeconds(10), maxWorkUnits: 20_000_000));
        Assert.True(outcome.Succeeded, id);

        // ---- value ----
        if (definition.Output.Returns is null)
            Assert.True(outcome.Value is null, $"{id} returned a value but declares none");
        else
        {
            var declared = definition.Output.Returns.Value;
            var problems = new List<string>();
            Conform(JsonSerializer.SerializeToElement(outcome.Value, ProtocolJson.Options), declared, "value", problems);
            Assert.True(problems.Count == 0, $"{id}: {string.Join("; ", problems)}");
        }

        // ---- steps ----
        foreach (var step in outcome.Trace)
        {
            var declared = definition.Output.Steps.SingleOrDefault(s => s.Type == step.StepType);
            Assert.True(declared is not null, $"{id} emitted step '{step.StepType}', which is not declared");

            if (declared!.PayloadType is null)
                Assert.True(step.Payload is null, $"{id}/{step.StepType} carries a payload but declares none");
            else
            {
                Assert.True(step.Payload is not null, $"{id}/{step.StepType} declares a payload but sent none");
                Assert.True(declared.PayloadType == step.Payload!.GetType(),
                    $"{id}/{step.StepType}: payload is {step.Payload.GetType().Name}, declared {declared.PayloadType.Name}");

                var problems = new List<string>();
                Conform(JsonSerializer.SerializeToElement(step.Payload, ProtocolJson.Options),
                    new Shape(null, "object", false, declared.Payload, null), "payload", problems);
                Assert.True(problems.Count == 0, $"{id}/{step.StepType}: {string.Join("; ", problems)}");
            }
        }
    }

    // ---------- checking JSON against a Shape ----------

    private static void Conform(JsonElement actual, Shape shape, string path, List<string> problems)
    {
        if (actual.ValueKind == JsonValueKind.Null)
        {
            if (!shape.Nullable && shape.Type != "any") problems.Add($"{path} is null but not declared nullable");
            return;
        }

        switch (shape.Type)
        {
            case "any":
                return;
            case "int":
                if (actual.ValueKind != JsonValueKind.Number || !actual.TryGetInt64(out _)) problems.Add($"{path} should be an integer");
                return;
            case "number":
                if (actual.ValueKind != JsonValueKind.Number) problems.Add($"{path} should be a number");
                return;
            case "bool":
                if (actual.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) problems.Add($"{path} should be a bool");
                return;
            case "text":
                if (actual.ValueKind != JsonValueKind.String) problems.Add($"{path} should be text");
                return;
            case "list":
                if (actual.ValueKind != JsonValueKind.Array) { problems.Add($"{path} should be a list"); return; }
                int index = 0;
                foreach (var item in actual.EnumerateArray().Take(50))
                    Conform(item, shape.Item!, $"{path}[{index++}]", problems);
                return;
            case "map":
                if (actual.ValueKind != JsonValueKind.Object) { problems.Add($"{path} should be a map"); return; }
                foreach (var property in actual.EnumerateObject().Take(50))
                    Conform(property.Value, shape.Item!, $"{path}.{property.Name}", problems);
                return;
            case "object":
                if (actual.ValueKind != JsonValueKind.Object) { problems.Add($"{path} should be an object"); return; }
                var wanted = shape.Fields!.Select(f => f.Name!).OrderBy(n => n, StringComparer.Ordinal).ToList();
                var present = actual.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                if (!wanted.SequenceEqual(present))
                {
                    problems.Add($"{path} has fields [{string.Join(",", present)}], declared [{string.Join(",", wanted)}]");
                    return;
                }
                foreach (var field in shape.Fields!)
                    Conform(actual.GetProperty(field.Name!), field, $"{path}.{field.Name}", problems);
                return;
            default:
                problems.Add($"{path}: unknown shape type '{shape.Type}'");
                return;
        }
    }
}
