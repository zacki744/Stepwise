using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Engine.Gateway.Tests;

public class OperationsEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetOperations_ReturnsEveryRegisteredOperation()
    {
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<List<OperationDto>>("/operations");

        Assert.NotNull(result);
        Assert.Equal(61, result!.Count);
        Assert.Contains(result, o => o.Id == "matrix-to-graph" && o.DatasetType == "matrix");
    }
    [Fact]
    public async Task GetOperations_EveryEntryCarriesExampleOptions()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/operations"));

        foreach (var entry in document.RootElement.EnumerateArray())
            Assert.Equal(JsonValueKind.Object, entry.GetProperty("exampleOptions").ValueKind);
    }

    [Fact]
    public async Task GetOperations_EveryEntryIsDescribed()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/operations"));

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("label").GetString()), id);
            Assert.NotEqual("Other", entry.GetProperty("category").GetString());
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()), id);
            Assert.Equal(JsonValueKind.Array, entry.GetProperty("options").ValueKind);
        }
    }

    [Fact]
    public async Task GetOperations_OptionsCarryKindRequiredAndDefault()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/operations"));
        var flip = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "flip");
        var axis = flip.GetProperty("options").EnumerateArray().Single();

        Assert.Equal("axis", axis.GetProperty("name").GetString());
        Assert.Equal("choice", axis.GetProperty("kind").GetString());
        Assert.False(axis.GetProperty("required").GetBoolean());
        Assert.Equal("horizontal", axis.GetProperty("default").GetString());
        Assert.Equal(new[] { "horizontal", "vertical" },
            axis.GetProperty("choices").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    private sealed record OperationDto(string Id, string DatasetType);
}