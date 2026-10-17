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
public sealed record OperationDefinition(IOperationHandler Handler, OperationDescriptor Descriptor, string ExampleJson);

internal static class Define
{
    /// <param name="descriptor">How the operation is presented and which options it takes.</param>
    /// <param name="exampleJson">A valid options object that includes every optional key.</param>
    /// <param name="create">Turns a request's (already validated) options into the operation to run.</param>
    public static OperationDefinition For<TDataset>(
        OperationDescriptor descriptor, string exampleJson,
        Func<IReadOnlyDictionary<string, JsonElement>, IOperation<TDataset>> create)
        where TDataset : class, IDataset
        => new(new OperationHandler<TDataset>(descriptor.Id, create), descriptor, exampleJson);
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

    private static List<OperationDefinition> Build()
    {
        var all = MatrixOperationDefinitions.All()
            .Concat(GraphOperationDefinitions.All())
            .Concat(TreeOperationDefinitions.All())
            .ToList();

        var duplicate = all.GroupBy(d => d.Descriptor.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Operation '{duplicate.Key}' is defined more than once");
        return all;
    }
}
