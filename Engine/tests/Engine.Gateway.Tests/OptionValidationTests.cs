using System.Text.Json;
using Engine.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Engine.Gateway.Tests;

public class OptionValidationTests
{
    public static IEnumerable<object[]> AllOperationIds() => OperationContractTests.AllOperationIds();

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static InvalidOptionsException Rejected(string operationId, Dictionary<string, JsonElement> options) =>
        Assert.Throws<InvalidOptionsException>(() => OptionValidator.Validate(OperationDescriptors.TryGet(operationId)!, options));

    private static void Accepted(string operationId, Dictionary<string, JsonElement> options) =>
        OptionValidator.Validate(OperationDescriptors.TryGet(operationId)!, options);

    // ---------- the three audit cases ----------

    [Fact]
    public void ATypoedOptionName_IsRejected_AndTheMessageListsTheRealOnes()
    {
        var error = Rejected("rotate", new() { ["quarterTurn"] = Json("2") });

        Assert.Contains("'quarterTurn'", error.Message);
        Assert.Contains("quarterTurns", error.Message);
    }

    [Fact]
    public void AnOperationWithoutOptions_RejectsAnyOption()
    {
        var error = Rejected("transpose", new() { ["times"] = Json("2") });

        Assert.Contains("takes no options", error.Message);
    }

    [Fact]
    public void AMissingRequiredOption_IsNamed()
    {
        var error = Rejected("set-cell", new() { ["row"] = Json("0"), ["column"] = Json("0") });

        Assert.Contains("'value'", error.Message);
    }

    // ---------- types and choices ----------

    [Theory]
    [InlineData("set-cell", "row", "\"0\"")]       // a string is not a whole number
    [InlineData("set-cell", "row", "1.5")]
    [InlineData("set-cell", "row", "99999999999")] // beyond int
    [InlineData("set-cell", "row", "null")]
    [InlineData("add-connection", "weight", "\"heavy\"")]
    [InlineData("add-connection", "directed", "1")]
    [InlineData("add-connection", "directed", "\"true\"")]
    [InlineData("flip", "axis", "\"diagonal\"")]
    [InlineData("flip", "axis", "\"Horizontal\"")] // choices are exact, as the operations themselves compare them
    [InlineData("flip", "axis", "3")]
    [InlineData("subgraph", "nodes", "5")]
    [InlineData("subgraph", "nodes", "[1,\"two\"]")]
    [InlineData("place-items", "items", "[{\"id\":1,\"width\":2}]")]  // height missing
    [InlineData("place-items", "items", "[1,2]")]
    public void WrongTypedOrOutOfListValues_AreRejected(string operationId, string name, string value)
    {
        var options = OperationExamples.Parse(operationId).ToDictionary(p => p.Key, p => p.Value);
        options[name] = Json(value);

        var error = Rejected(operationId, options);

        Assert.Contains($"'{name}'", error.Message);
    }

    [Fact]
    public void ChoiceErrors_ListTheChoices()
    {
        var error = Rejected("flip", new() { ["axis"] = Json("\"sideways\"") });

        Assert.Contains("horizontal", error.Message);
        Assert.Contains("vertical", error.Message);
    }

    [Fact]
    public void OptionalOptions_MayBeLeftOut_AndLargeButValidNumbersPass()
    {
        Accepted("rotate", new());
        Accepted("random-fill", new() { ["seed"] = Json("2147483647") });
        Accepted("add-connection", new() { ["from"] = Json("0"), ["to"] = Json("1"), ["weight"] = Json("-2.5") });
    }

    // ---------- across the whole catalog ----------

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void EveryExample_PassesValidation(string id) => Accepted(id, OperationExamples.Parse(id).ToDictionary(p => p.Key, p => p.Value));

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void EveryOption_RejectsAnUnrelatedString(string id)
    {
        foreach (var option in OperationDescriptors.TryGet(id)!.Options)
        {
            var options = OperationExamples.Parse(id).ToDictionary(p => p.Key, p => p.Value);
            options[option.Name] = Json("\"zzz-not-a-valid-value\"");

            Assert.Contains($"'{option.Name}'", Rejected(id, options).Message);
        }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void EveryOption_RejectsNull(string id)
    {
        foreach (var option in OperationDescriptors.TryGet(id)!.Options)
        {
            var options = OperationExamples.Parse(id).ToDictionary(p => p.Key, p => p.Value);
            options[option.Name] = Json("null");

            Assert.Contains("must not be null", Rejected(id, options).Message);
        }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void EveryRequiredOption_IsDemanded(string id)
    {
        foreach (var option in OperationDescriptors.TryGet(id)!.Options.Where(o => o.Required))
        {
            var options = OperationExamples.Parse(id).Where(p => p.Key != option.Name).ToDictionary(p => p.Key, p => p.Value);

            Assert.Contains($"'{option.Name}'", Rejected(id, options).Message);
        }
    }

    [Theory]
    [MemberData(nameof(AllOperationIds))]
    public void TheServersRegistry_ValidatesBeforeRunning(string id)
    {
        var handler = OperationCatalog.BuildDefault().TryGet(id)!;
        var sessions = new Engine.Session.SessionManager(maxSessions: 1);
        var session = sessions.Create(OperationContractTests.NewDataset(handler.DatasetType));
        var options = OperationExamples.Parse(id).ToDictionary(p => p.Key, p => p.Value);
        options["notAnOption"] = Json("1");

        Assert.Throws<InvalidOptionsException>(() => handler.Execute(
            sessions, session.Id, options, Engine.Domain.Operations.OperationContext.Unbounded));
    }

    // ---------- over the wire ----------

    [Fact]
    public async Task OverTheWire_ATypoIsAnOperationFailed_NotASilentDefault()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        await GatewayTestHelpers.SendAsync(socket, new ExecuteOperationMessage(Guid.NewGuid(), "rotate",
            new Dictionary<string, JsonElement> { ["quarterTurn"] = Json("2") }));

        var failed = Assert.IsType<OperationFailedMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.StartsWith("Invalid options:", failed.Reason);
        Assert.Contains("quarterTurns", failed.Reason);
    }
}
