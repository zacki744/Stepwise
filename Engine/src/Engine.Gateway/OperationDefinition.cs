using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Operations;
using Engine.Session;

namespace Engine.Gateway;

/// <summary>
/// Everything about one operation in one place: the handler that builds and runs it, how it is described to
/// clients (label, category, options), and one valid example. The descriptor list, the examples served by
/// GET /operations and the registry the server runs are all read from the definitions, so they cannot disagree.
/// </summary>
public sealed record OperationDefinition(IOperationHandler Handler, OperationDescriptor Descriptor, string ExampleJson)
{
    /// <summary>How the operation treats the session's dataset, read from the operation itself (not declared twice).</summary>
    public StateEffect Effect { get; init; }

    /// <summary>What the operation returns and which steps it narrates (see <see cref="OperationOutputs"/>).</summary>
    public OperationOutput Output { get; init; } = new(null, []);
}

internal static class Define
{
    /// <param name="descriptor">How the operation is presented and which options it takes.</param>
    /// <param name="exampleJson">A valid options object that includes every optional key.</param>
    /// <param name="create">Turns a request's (already validated) options into the operation to run.</param>
    public static OperationDefinition For<TDataset>(
        OperationDescriptor descriptor, string exampleJson,
        Func<IReadOnlyDictionary<string, JsonElement>, IOperation<TDataset>> create)
        where TDataset : class, IDataset
        => new(new OperationHandler<TDataset>(descriptor.Id, create), descriptor, exampleJson)
        {
            Effect = create(ParseExample(exampleJson)).Metadata.StateEffect
        };

    private static Dictionary<string, JsonElement> ParseExample(string exampleJson)
    {
        using var document = JsonDocument.Parse(exampleJson);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}

/// <summary>Shorthands for writing definitions: <c>using static Engine.Gateway.OperationDsl;</c></summary>
internal static class OperationDsl
{
    // Categories (what a UI groups by). "Conversion" is spelled out because Convert would clash with System.Convert.
    public const string Edit = "Edit", Reshape = "Reshape", Query = "Query", Generate = "Generate", Conversion = "Convert";
    public const string Inspect = "Inspect", Paths = "Paths and flow";

    public static OptionDescriptor Req(string name, string kind, string label) => new(name, kind, label, true);
    public static OptionDescriptor Opt(string name, string kind, string label, object? def) => new(name, kind, label, false, def);
    public static OptionDescriptor Pick(string name, string label, string def, params string[] choices) =>
        new(name, OptionKinds.Choice, label, false, def, choices);

    public static OperationDescriptor Op(string id, string label, string category, string description,
        params OptionDescriptor[] options) => new(id, label, category, description, options);
}

/// <summary>The complete list of operations, in the order clients see them (matrix, graph, tree).</summary>
public static class OperationDefinitions
{
    public static IReadOnlyList<OperationDefinition> All { get; } = Build();

    private static readonly Dictionary<string, OperationDefinition> ById = All.ToDictionary(d => d.Descriptor.Id);

    public static OperationDefinition? TryGet(string operationId) => ById.GetValueOrDefault(operationId);

    private static List<OperationDefinition> Build()
    {
        var all = MatrixOperationDefinitions.All()
            .Concat(GraphOperationDefinitions.All())
            .Concat(TreeOperationDefinitions.All())
            .ToList();

        var outputs = OperationOutputs.ById;
        var unknown = outputs.Keys.Except(all.Select(d => d.Descriptor.Id)).FirstOrDefault();
        if (unknown is not null)
            throw new InvalidOperationException($"Outputs are declared for '{unknown}', which is not an operation");
        for (int i = 0; i < all.Count; i++)
        {
            var id = all[i].Descriptor.Id;
            if (!outputs.TryGetValue(id, out var output))
                throw new InvalidOperationException($"Operation '{id}' has no declared output in OperationOutputs");
            all[i] = all[i] with { Output = output };
        }

        var duplicate = all.GroupBy(d => d.Descriptor.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Operation '{duplicate.Key}' is defined more than once");
        return all;
    }
}
