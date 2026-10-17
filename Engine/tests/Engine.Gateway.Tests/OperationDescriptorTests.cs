using System.Text.Json;
using Engine.Session;
using Xunit;

namespace Engine.Gateway.Tests;

/// <summary>
/// Keeps the hand-written descriptors honest. A descriptor that lies (wrong option name, a "required"
/// flag the handler disagrees with, a default of the wrong type) would send a UI building broken forms,
/// so each claim is checked against OperationExamples and the real handlers.
/// </summary>
public class OperationDescriptorTests
{
    private static readonly OperationRegistry Registry = OperationCatalog.BuildDefault();
    private static readonly OperationRegistry BareRegistry = OperationCatalog.BuildUnvalidated();

    public static IEnumerable<object[]> AllOperationIds() => OperationContractTests.AllOperationIds();

    private static OperationDescriptor Descriptor(string id) =>
        OperationDescriptors.TryGet(id) ?? throw new Xunit.Sdk.XunitException($"No descriptor for '{id}'");

    [Fact]
    public void DescriptorsExistForExactlyTheRegisteredOperations()
    {
        var registered = Registry.All.Select(h => h.OperationId).OrderBy(x => x).ToList();
        var described = OperationDescriptors.All.Select(d => d.Id).OrderBy(x => x).ToList();

        Assert.Equal(registered, described);
    }

    [Fact]
    public void EveryDescriptorHasLabelCategoryAndDescription()
    {
        foreach (var d in OperationDescriptors.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Label), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Category), d.Id);
            Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Id);
            Assert.Equal(d.Options.Count, d.Options.Select(o => o.Name).Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void OptionNames_MatchTheExample(string id)
    {
        var described = Descriptor(id).Options.Select(o => o.Name).OrderBy(x => x);
        var example = OperationExamples.Parse(id).Keys.OrderBy(x => x);

        Assert.Equal(example, described);
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void ExampleValues_MatchTheDeclaredKinds(string id)
    {
        var example = OperationExamples.Parse(id);
        foreach (var option in Descriptor(id).Options)
            AssertMatchesKind(option, example[option.Name], $"{id}.{option.Name}");
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void Defaults_AreOnlyOnOptionalOptions_AndMatchTheirKind(string id)
    {
        foreach (var option in Descriptor(id).Options)
        {
            if (option.Required)
            {
                Assert.Null(option.Default);
                continue;
            }
            if (option.Default is null) continue; // "not set" is a legitimate default, e.g. count-regions' value filter

            var element = JsonSerializer.SerializeToElement(option.Default);
            AssertMatchesKind(option, element, $"{id}.{option.Name} default");
        }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void RequiredFlags_MatchWhatTheHandlerReallyDemands(string id)
    {
        var handler = BareRegistry.TryGet(id)!; // the validator would make every "required" claim true by itself
        var example = OperationExamples.Parse(id);

        foreach (var option in Descriptor(id).Options)
        {
            var without = example.Where(p => p.Key != option.Name).ToDictionary(p => p.Key, p => p.Value);
            var failed = Fails(handler, without);

            Assert.True(option.Required == failed,
                option.Required
                    ? $"{id}.{option.Name} is declared required, but the handler accepts the options without it"
                    : $"{id}.{option.Name} is declared optional, but the handler fails without it");
        }
    }

    private static bool Fails(IOperationHandler handler, IReadOnlyDictionary<string, JsonElement> options)
    {
        var sessions = new SessionManager(maxSessions: 1);
        var session = sessions.Create(OperationContractTests.NewDataset(handler.DatasetType));
        try
        {
            handler.Execute(sessions, session.Id, options,
                new Engine.Domain.Operations.OperationContext(
                    CancellationToken.None, TimeSpan.FromSeconds(10), maxWorkUnits: 20_000_000));
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void AssertMatchesKind(OptionDescriptor option, JsonElement value, string where)
    {
        switch (option.Kind)
        {
            case OptionKinds.Int or OptionKinds.Row or OptionKinds.Column or OptionKinds.Node or OptionKinds.Seed:
                Assert.True(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _), $"{where}: expected an integer");
                break;
            case OptionKinds.Number:
                Assert.Equal(JsonValueKind.Number, value.ValueKind);
                break;
            case OptionKinds.Bool:
                Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{where}: expected a boolean");
                break;
            case OptionKinds.Choice:
                Assert.NotNull(option.Choices);
                Assert.Equal(JsonValueKind.String, value.ValueKind);
                Assert.Contains(value.GetString(), option.Choices!);
                break;
            case OptionKinds.NodeList:
                Assert.Equal(JsonValueKind.Array, value.ValueKind);
                Assert.All(value.EnumerateArray(), e => Assert.True(e.TryGetInt32(out _), $"{where}: expected integers"));
                break;
            case OptionKinds.ItemList:
                Assert.Equal(JsonValueKind.Array, value.ValueKind);
                Assert.All(value.EnumerateArray(), e => Assert.Equal(JsonValueKind.Object, e.ValueKind));
                break;
            default:
                Assert.Fail($"{where}: unknown option kind '{option.Kind}'");
                break;
        }

        if (option.Kind != OptionKinds.Choice)
            Assert.Null(option.Choices);
    }
}
