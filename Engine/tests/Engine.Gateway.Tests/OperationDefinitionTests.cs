using Xunit;

namespace Engine.Gateway.Tests;

/// <summary>An operation is defined once; these check that the views derived from that one definition line up.</summary>
public class OperationDefinitionTests
{
    [Fact]
    public void EveryDefinition_HasMatchingHandlerAndDescriptorIds()
    {
        foreach (var definition in OperationDefinitions.All)
            Assert.Equal(definition.Descriptor.Id, definition.Handler.OperationId);
    }

    [Fact]
    public void TheServerRegistry_HoldsExactlyTheDefinedOperations()
    {
        var defined = OperationDefinitions.All.Select(d => d.Descriptor.Id).OrderBy(x => x);
        var registered = OperationCatalog.BuildDefault().All.Select(h => h.OperationId).OrderBy(x => x);

        Assert.Equal(defined, registered);
    }

    [Fact]
    public void DescriptorsAndExamples_AreServedForExactlyTheDefinedOperations()
    {
        var defined = OperationDefinitions.All.Select(d => d.Descriptor.Id).OrderBy(x => x).ToList();

        Assert.Equal(defined, OperationDescriptors.All.Select(d => d.Id).OrderBy(x => x));
        Assert.Equal(defined, OperationExamples.OperationIds.OrderBy(x => x));
    }

    [Fact]
    public void Definitions_AreGroupedByDatasetType_InTheOrderClientsSeeThem()
    {
        var types = OperationDefinitions.All.Select(d => d.Handler.DatasetType).ToList();

        // matrix operations first, then graph, then tree, with no interleaving
        Assert.Equal(types.OrderBy(t => Array.IndexOf(["matrix", "graph", "tree"], t)), types);
    }

    [Fact]
    public void EveryExample_IsAJsonObject()
    {
        foreach (var definition in OperationDefinitions.All)
        {
            var example = OperationExamples.TryGetElement(definition.Descriptor.Id);
            Assert.True(example is { ValueKind: System.Text.Json.JsonValueKind.Object }, definition.Descriptor.Id);
        }
    }
}
